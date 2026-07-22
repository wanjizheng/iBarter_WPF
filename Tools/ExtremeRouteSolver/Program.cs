using System.Text.Json;
using Google.OrTools.Sat;
using iBarter.Routing;

if (args.Length != 2) {
    Console.Error.WriteLine("usage: iBarter.ExtremeRouteSolver <input.json> <output.json>");
    return 2;
}

try {
    var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
    var input = JsonSerializer.Deserialize<ExtremeSolverInputDto>(
        await File.ReadAllTextAsync(args[0]), jsonOptions)
        ?? throw new InvalidDataException("The solver input is empty.");
    var engine = new ExtremeCpSatEngine(input, args[1], jsonOptions);
    return engine.Solve();
}
catch (Exception ex) {
    try {
        var failure = new ExtremeSolverOutputDto(
            ExtremeRouteSolverProtocol.Version, "Error", 0, 0, 0, 1, 0, 0, 0, [],
            0, 0,
            $"{ex.GetType().Name}: {ex.Message}");
        await WriteAtomicAsync(args[1], JsonSerializer.Serialize(failure));
    }
    catch { }
    Console.Error.WriteLine(ex);
    return 1;
}

static async Task WriteAtomicAsync(string path, string content) {
    string temporary = path + ".tmp";
    await File.WriteAllTextAsync(temporary, content);
    File.Move(temporary, path, true);
}

internal sealed class ExtremeCpSatEngine {
    private readonly ExtremeSolverInputDto input;
    private readonly string outputPath;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly int n;
    private readonly int wCount;
    private readonly int itemCount;
    private readonly Dictionary<string, int> itemIndex;
    private readonly Dictionary<string, int> warehouseIndex;

    private CpModel model = null!;
    private BoolVar[,,] taskAt = null!;
    private BoolVar[,] slotUsed = null!;
    private IntVar[,] nodeAt = null!;
    private BoolVar[,] startChoice = null!;
    private BoolVar[,] endChoice = null!;
    private BoolVar[] routeUsed = null!;
    private BoolVar[] hasPickup = null!;
    private IntVar[,,] pickup = null!;
    private IntVar totalDistance = null!;

    public ExtremeCpSatEngine(
        ExtremeSolverInputDto input,
        string outputPath,
        JsonSerializerOptions jsonOptions) {
        this.input = input;
        this.outputPath = outputPath;
        this.jsonOptions = jsonOptions;
        n = input.Tasks.Count;
        wCount = input.Warehouses.Count;
        itemCount = input.Items.Count;
        itemIndex = input.Items.Select((item, index) => (item.ItemId, index))
            .ToDictionary(x => x.ItemId, x => x.index, StringComparer.Ordinal);
        warehouseIndex = input.Warehouses.Select((warehouse, index) => (warehouse.WarehouseId, index))
            .ToDictionary(x => x.WarehouseId, x => x.index, StringComparer.Ordinal);
    }

    public int Solve() {
        Validate();
        BuildModel();
        ApplyHints();
        string validationError = model.Validate();
        if (!string.IsNullOrWhiteSpace(validationError))
            throw new InvalidDataException($"CP-SAT model validation failed: {validationError}");

        var parameters = new List<string> {
            $"max_time_in_seconds:{input.TimeLimitSeconds}",
            $"max_memory_in_mb:{input.MemoryLimitMb}",
            $"num_workers:{input.WorkerCount}",
            "log_search_progress:false",
            "cp_model_presolve:true",
            "use_optimization_hints:true",
        };
        // Extreme is an anytime "best route within ten minutes" mode. Once a
        // verified seed exists, dedicate the CP-SAT portfolio to large-neighborhood
        // improvement instead of spending most workers proving a very weak bound.
        if (input.InitialRoutes.Count > 0)
            parameters.Add("use_lns_only:true");
        if (Environment.GetEnvironmentVariable("IBARTER_EXTREME_DIAGNOSTICS") == "1") {
            parameters.Remove("log_search_progress:false");
            parameters.Add("log_search_progress:true");
            parameters.Add("log_to_response:true");
            parameters.Add("log_to_stdout:false");
        }
        var solver = new CpSolver {
            StringParameters = string.Join(" ", parameters),
        };
        var writer = new FinalSolutionWriter(
            input, outputPath, jsonOptions, taskAt, slotUsed, startChoice, endChoice,
            routeUsed, hasPickup, pickup, totalDistance, warehouseIndex);
        // The Windows x64 helper showed nondeterministic native fatal exits
        // while the managed callback walked thousands of variables. This is a
        // bounded optimization run, so CP-SAT returns its best feasible result
        // at the time limit and the intermediate callback is unnecessary.
        CpSolverStatus status = solver.Solve(model);
        writer.WriteFinal(solver, status);
        return status is CpSolverStatus.Optimal or CpSolverStatus.Feasible ? 0 : 3;
    }

    private void Validate() {
        if (input.ProtocolVersion != ExtremeRouteSolverProtocol.Version)
            throw new InvalidDataException($"Unsupported protocol {input.ProtocolVersion}.");
        if (n is <= 0 or > ExtremeRouteSolverProtocol.MaximumTasks)
            throw new InvalidDataException($"Task count {n} is outside 1..{ExtremeRouteSolverProtocol.MaximumTasks}.");
        if (input.MaxRoutes is <= 0 || input.MaxRoutes > n)
            throw new InvalidDataException($"Route limit {input.MaxRoutes} is outside 1..{n}.");
        if (wCount == 0 || itemCount == 0)
            throw new InvalidDataException("At least one warehouse and one item are required.");
        if (input.CargoCapacityLT < 0)
            throw new InvalidDataException("Cargo capacity must be non-negative.");
        ValidateMatrix(input.TaskToTaskDistance, n, n, nameof(input.TaskToTaskDistance));
        ValidateMatrix(input.WarehouseToTaskDistance, wCount, n, nameof(input.WarehouseToTaskDistance));
        ValidateMatrix(input.TaskToWarehouseDistance, n, wCount, nameof(input.TaskToWarehouseDistance));
        ValidateMatrix(input.WarehouseToWarehouseDistance, wCount, wCount, nameof(input.WarehouseToWarehouseDistance));
        foreach (var task in input.Tasks) {
            if (!itemIndex.ContainsKey(task.Item1Id) || !itemIndex.ContainsKey(task.Item2Id))
                throw new InvalidDataException($"Task {task.RowId} references an unknown item.");
        }
    }

    private static void ValidateMatrix(
        IReadOnlyList<IReadOnlyList<long>> matrix, int rows, int columns, string name) {
        if (matrix.Count != rows || matrix.Any(row => row.Count != columns || row.Any(value => value < 0)))
            throw new InvalidDataException($"Invalid {name} matrix.");
    }

    private void BuildModel() {
        model = new CpModel();
        int maxRoutes = input.MaxRoutes;
        taskAt = new BoolVar[maxRoutes, n, n];
        slotUsed = new BoolVar[maxRoutes, n];
        nodeAt = new IntVar[maxRoutes, n];
        startChoice = new BoolVar[maxRoutes, wCount];
        endChoice = new BoolVar[maxRoutes, wCount];
        routeUsed = new BoolVar[maxRoutes];
        hasPickup = new BoolVar[maxRoutes];
        pickup = new IntVar[maxRoutes, wCount, itemCount];

        for (int r = 0; r < maxRoutes; r++) {
            routeUsed[r] = model.NewBoolVar($"route_{r}");
            hasPickup[r] = model.NewBoolVar($"has_pickup_{r}");
            for (int p = 0; p < n; p++) {
                slotUsed[r, p] = model.NewBoolVar($"used_{r}_{p}");
                nodeAt[r, p] = model.NewIntVar(0, n, $"node_{r}_{p}");
                var atThisSlot = new List<ILiteral>(n);
                LinearExpr node = LinearExpr.Constant(0);
                for (int t = 0; t < n; t++) {
                    taskAt[r, p, t] = model.NewBoolVar($"at_{r}_{p}_{t}");
                    atThisSlot.Add(taskAt[r, p, t]);
                    node += (t + 1) * taskAt[r, p, t];
                }
                model.Add(LinearExpr.Sum(atThisSlot) == slotUsed[r, p]);
                model.Add(nodeAt[r, p] == node);
                if (p > 0) model.Add(slotUsed[r, p] <= slotUsed[r, p - 1]);
            }
            model.Add(routeUsed[r] == slotUsed[r, 0]);
            if (r > 0) {
                model.Add(routeUsed[r] <= routeUsed[r - 1]);
                model.Add(hasPickup[r] == routeUsed[r]);
            } else {
                model.Add(hasPickup[r] <= routeUsed[r]);
            }

            var starts = new List<ILiteral>(wCount);
            var ends = new List<ILiteral>(wCount);
            for (int w = 0; w < wCount; w++) {
                startChoice[r, w] = model.NewBoolVar($"start_{r}_{w}");
                endChoice[r, w] = model.NewBoolVar($"end_{r}_{w}");
                starts.Add(startChoice[r, w]);
                ends.Add(endChoice[r, w]);
            }
            model.Add(LinearExpr.Sum(starts) == hasPickup[r]);
            model.Add(LinearExpr.Sum(ends) == routeUsed[r]);
        }

        for (int t = 0; t < n; t++) {
            var placements = new List<ILiteral>(maxRoutes * n);
            for (int r = 0; r < maxRoutes; r++)
                for (int p = 0; p < n; p++)
                    placements.Add(taskAt[r, p, t]);
            model.AddExactlyOne(placements);
        }

        BuildInventoryAndCapacity();
        BuildDistanceObjective();
    }

    private void ApplyHints() {
        for (int r = 0; r < input.InitialRoutes.Count && r < input.MaxRoutes; r++) {
            ExtremeSolverRouteDto route = input.InitialRoutes[r];
            model.AddHint(routeUsed[r], 1);
            model.AddHint(hasPickup[r], route.StartWarehouseId is null ? 0 : 1);
            for (int w = 0; w < wCount; w++) {
                model.AddHint(startChoice[r, w], route.StartWarehouseId == input.Warehouses[w].WarehouseId ? 1 : 0);
                model.AddHint(endChoice[r, w], route.EndWarehouseId == input.Warehouses[w].WarehouseId ? 1 : 0);
            }
            for (int p = 0; p < n; p++) {
                bool occupied = p < route.TaskIndices.Count;
                model.AddHint(slotUsed[r, p], occupied ? 1 : 0);
                for (int t = 0; t < n; t++)
                    model.AddHint(taskAt[r, p, t], occupied && route.TaskIndices[p] == t ? 1 : 0);
            }
            if (route.StartWarehouseId is { } start && warehouseIndex.TryGetValue(start, out int startIndex)) {
                var quantities = route.Pickup.ToDictionary(x => x.ItemId, x => x.Quantity, StringComparer.Ordinal);
                for (int item = 0; item < itemCount; item++)
                    model.AddHint(pickup[r, startIndex, item], quantities.GetValueOrDefault(input.Items[item].ItemId));
            }
        }
        for (int r = input.InitialRoutes.Count; r < input.MaxRoutes; r++) {
            model.AddHint(routeUsed[r], 0);
            model.AddHint(hasPickup[r], 0);
        }
    }

    private void BuildInventoryAndCapacity() {
        int maxRoutes = input.MaxRoutes;
        var stock = new IntVar[maxRoutes + 1, wCount, itemCount];
        var unload = new IntVar[maxRoutes, wCount, itemCount];
        var cargoAfter = new IntVar[maxRoutes, n, itemCount];

        for (int w = 0; w < wCount; w++) {
            for (int item = 0; item < itemCount; item++) {
                long upper = input.Items[item].QuantityUpperBound;
                stock[0, w, item] = model.NewIntVar(0, upper, $"stock_0_{w}_{item}");
                model.Add(stock[0, w, item] == input.Warehouses[w].Inventory.GetValueOrDefault(input.Items[item].ItemId));
            }
        }

        for (int r = 0; r < maxRoutes; r++) {
            var startCargo = new LinearExpr[itemCount];
            for (int item = 0; item < itemCount; item++) {
                long upper = input.Items[item].QuantityUpperBound;
                LinearExpr picked = LinearExpr.Constant(0);
                for (int w = 0; w < wCount; w++) {
                    pickup[r, w, item] = model.NewIntVar(0, upper, $"pickup_{r}_{w}_{item}");
                    model.Add(pickup[r, w, item] <= upper * startChoice[r, w]);
                    model.Add(pickup[r, w, item] <= stock[r, w, item]);
                    picked += pickup[r, w, item];
                }
                long initial = r == 0 ? input.InitialOnBoard.GetValueOrDefault(input.Items[item].ItemId) : 0;
                startCargo[item] = picked + initial;
            }

            LinearExpr pickupCount = LinearExpr.Constant(0);
            for (int w = 0; w < wCount; w++)
                for (int item = 0; item < itemCount; item++)
                    pickupCount += pickup[r, w, item];
            model.Add(pickupCount >= hasPickup[r]);
            model.Add(WeightedSum(startCargo) <= input.CargoCapacityLT);

            for (int p = 0; p < n; p++) {
                for (int item = 0; item < itemCount; item++) {
                    long upper = input.Items[item].QuantityUpperBound;
                    string itemId = input.Items[item].ItemId;
                    LinearExpr before = p == 0 ? startCargo[item] : cargoAfter[r, p - 1, item];
                    LinearExpr consumed = LinearExpr.Constant(0);
                    LinearExpr delta = LinearExpr.Constant(0);
                    for (int t = 0; t < n; t++) {
                        var task = input.Tasks[t];
                        if (task.Item1Id == itemId) {
                            consumed += task.InputQuantity * taskAt[r, p, t];
                            delta -= task.InputQuantity * taskAt[r, p, t];
                        }
                        if (task.Item2Id == itemId)
                            delta += task.OutputQuantity * taskAt[r, p, t];
                    }
                    model.Add(before >= consumed);
                    cargoAfter[r, p, item] = model.NewIntVar(0, upper, $"cargo_{r}_{p}_{item}");
                    model.Add(cargoAfter[r, p, item] == before + delta);
                }
                model.Add(WeightedSum(Enumerable.Range(0, itemCount)
                    .Select(item => (LinearExpr)cargoAfter[r, p, item]).ToArray()) <= input.CargoCapacityLT);
            }

            for (int w = 0; w < wCount; w++) {
                for (int item = 0; item < itemCount; item++) {
                    long upper = input.Items[item].QuantityUpperBound;
                    stock[r + 1, w, item] = model.NewIntVar(0, upper, $"stock_{r + 1}_{w}_{item}");
                    unload[r, w, item] = model.NewIntVar(0, upper, $"unload_{r}_{w}_{item}");
                    if (input.Items[item].UnitWeight == 0) {
                        model.Add(unload[r, w, item] == 0);
                    } else {
                        IntVar finalCargo = cargoAfter[r, n - 1, item];
                        model.Add(unload[r, w, item] <= finalCargo);
                        model.Add(unload[r, w, item] <= upper * endChoice[r, w]);
                        model.Add(unload[r, w, item] >= finalCargo - upper * (1 - endChoice[r, w]));
                    }
                    model.Add(stock[r + 1, w, item] ==
                        stock[r, w, item] - pickup[r, w, item] + unload[r, w, item]);
                }
            }
        }
    }

    private LinearExpr WeightedSum(IReadOnlyList<LinearExpr> quantities) {
        LinearExpr result = LinearExpr.Constant(0);
        for (int item = 0; item < itemCount; item++)
            if (input.Items[item].UnitWeight != 0)
                result += input.Items[item].UnitWeight * quantities[item];
        return result;
    }

    private void BuildDistanceObjective() {
        var distanceParts = new List<IntVar>();
        int noneStartCode = wCount;
        long maxLeg = input.TaskToTaskDistance.SelectMany(x => x)
            .Concat(input.WarehouseToTaskDistance.SelectMany(x => x))
            .Concat(input.TaskToWarehouseDistance.SelectMany(x => x))
            .Concat(input.WarehouseToWarehouseDistance.SelectMany(x => x))
            .DefaultIfEmpty(0).Max();

        var startTupleList = new List<long[]>();
        for (int w = 0; w < wCount; w++)
            for (int t = 0; t < n; t++)
                startTupleList.Add([w, t + 1, input.WarehouseToTaskDistance[w][t]]);
        startTupleList.AddRange(Enumerable.Range(1, n).Select(t => new long[] { noneStartCode, t, 0 }));
        startTupleList.AddRange(Enumerable.Range(0, wCount + 1).Select(w => new long[] { w, 0, 0 }));
        long[,] startTuples = TupleMatrix(startTupleList);

        var legTupleList = new List<long[]> { new long[] { 0, 0, 0 } };
        for (int from = 0; from < n; from++) {
            legTupleList.Add([from + 1, 0, 0]);
            for (int to = 0; to < n; to++)
                legTupleList.Add([from + 1, to + 1, input.TaskToTaskDistance[from][to]]);
        }
        long[,] legTuples = TupleMatrix(legTupleList);

        var endTupleList = new List<long[]>();
        for (int t = 0; t < n; t++)
            for (int w = 0; w < wCount; w++)
                endTupleList.Add([t + 1, w, input.TaskToWarehouseDistance[t][w]]);
        endTupleList.AddRange(Enumerable.Range(0, wCount).Select(w => new long[] { 0, w, 0 }));
        long[,] endTuples = TupleMatrix(endTupleList);

        var relocationTupleList = new List<long[]>();
        for (int from = 0; from < wCount; from++) {
            relocationTupleList.Add([from, noneStartCode, 0]);
            for (int to = 0; to < wCount; to++)
                relocationTupleList.Add([from, to, input.WarehouseToWarehouseDistance[from][to]]);
        }
        long[,] relocationTuples = TupleMatrix(relocationTupleList);

        for (int r = 0; r < input.MaxRoutes; r++) {
            IntVar startCode = model.NewIntVar(0, wCount, $"start_code_{r}");
            LinearExpr startExpr = noneStartCode * (1 - hasPickup[r]);
            for (int w = 0; w < wCount; w++) startExpr += w * startChoice[r, w];
            model.Add(startCode == startExpr);

            IntVar startDistance = model.NewIntVar(0, maxLeg, $"start_distance_{r}");
            AddAllowed([startCode, nodeAt[r, 0], startDistance], startTuples);
            distanceParts.Add(startDistance);

            for (int p = 1; p < n; p++) {
                IntVar leg = model.NewIntVar(0, maxLeg, $"leg_{r}_{p}");
                AddAllowed([nodeAt[r, p - 1], nodeAt[r, p], leg], legTuples);
                distanceParts.Add(leg);
            }

            IntVar lastNode = model.NewIntVar(0, n, $"last_node_{r}");
            var lastParts = new List<IntVar>(n);
            for (int p = 0; p < n; p++) {
                BoolVar isLast = model.NewBoolVar($"last_{r}_{p}");
                if (p == n - 1)
                    model.Add(isLast == slotUsed[r, p]);
                else
                    model.Add(isLast == slotUsed[r, p] - slotUsed[r, p + 1]);
                IntVar lastPart = model.NewIntVar(0, n, $"last_part_{r}_{p}");
                model.AddMultiplicationEquality(lastPart, [isLast, nodeAt[r, p]]);
                lastParts.Add(lastPart);
            }
            model.Add(lastNode == LinearExpr.Sum(lastParts));

            IntVar endCode = model.NewIntVar(0, Math.Max(0, wCount - 1), $"end_code_{r}");
            LinearExpr endExpr = LinearExpr.Constant(0);
            for (int w = 0; w < wCount; w++) endExpr += w * endChoice[r, w];
            model.Add(endCode == endExpr);
            IntVar endDistance = model.NewIntVar(0, maxLeg, $"end_distance_{r}");
            AddAllowed([lastNode, endCode, endDistance], endTuples);
            distanceParts.Add(endDistance);

            if (r > 0) {
                IntVar previousEnd = model.NewIntVar(0, Math.Max(0, wCount - 1), $"previous_end_{r}");
                LinearExpr previousEndExpr = LinearExpr.Constant(0);
                for (int w = 0; w < wCount; w++) previousEndExpr += w * endChoice[r - 1, w];
                model.Add(previousEnd == previousEndExpr);
                IntVar relocation = model.NewIntVar(0, maxLeg, $"relocation_{r}");
                AddAllowed([previousEnd, startCode, relocation], relocationTuples);
                distanceParts.Add(relocation);
            }
        }

        long maxDistance = checked(maxLeg * (long)(n * n + n * 3));
        totalDistance = model.NewIntVar(0, maxDistance, "total_distance");
        model.Add(totalDistance == LinearExpr.Sum(distanceParts));
        // The user-facing contract is the sum of sailing distance across all
        // routes. Keeping route count and pickup quantity inside one weighted
        // integer objective caused post-presolve coefficient overflow on real
        // 25+ task data. Secondary preferences are applied after replay when
        // two plans have the same distance.
        model.Minimize(totalDistance);
    }

    private void AddAllowed(IEnumerable<LinearExpr> expressions, long[,] tuples) {
        TableConstraint table = model.AddAllowedAssignments(expressions);
        table.AddTuples(tuples);
    }

    private static long[,] TupleMatrix(IReadOnlyList<long[]> tuples) {
        if (tuples.Count == 0) return new long[0, 0];
        int columns = tuples[0].Length;
        var result = new long[tuples.Count, columns];
        for (int row = 0; row < tuples.Count; row++)
            for (int column = 0; column < columns; column++)
                result[row, column] = tuples[row][column];
        return result;
    }
}

internal sealed class FinalSolutionWriter {
    private readonly ExtremeSolverInputDto input;
    private readonly string outputPath;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly BoolVar[,,] taskAt;
    private readonly BoolVar[,] slotUsed;
    private readonly BoolVar[,] startChoice;
    private readonly BoolVar[,] endChoice;
    private readonly BoolVar[] routeUsed;
    private readonly BoolVar[] hasPickup;
    private readonly IntVar[,,] pickup;
    private readonly IntVar totalDistance;
    private readonly Dictionary<string, int> warehouseIndex;
    public FinalSolutionWriter(
        ExtremeSolverInputDto input,
        string outputPath,
        JsonSerializerOptions jsonOptions,
        BoolVar[,,] taskAt,
        BoolVar[,] slotUsed,
        BoolVar[,] startChoice,
        BoolVar[,] endChoice,
        BoolVar[] routeUsed,
        BoolVar[] hasPickup,
        IntVar[,,] pickup,
        IntVar totalDistance,
        Dictionary<string, int> warehouseIndex) {
        this.input = input;
        this.outputPath = outputPath;
        this.jsonOptions = jsonOptions;
        this.taskAt = taskAt;
        this.slotUsed = slotUsed;
        this.startChoice = startChoice;
        this.endChoice = endChoice;
        this.routeUsed = routeUsed;
        this.hasPickup = hasPickup;
        this.pickup = pickup;
        this.totalDistance = totalDistance;
        this.warehouseIndex = warehouseIndex;
    }

    public void WriteFinal(CpSolver solver, CpSolverStatus status) {
        string statusText = status switch {
            CpSolverStatus.Optimal => "Optimal",
            CpSolverStatus.Feasible => "Feasible",
            CpSolverStatus.Infeasible => "Infeasible",
            CpSolverStatus.ModelInvalid => "ModelInvalid",
            _ => "Unknown",
        };
        if (status is CpSolverStatus.Optimal or CpSolverStatus.Feasible) {
            Write(Capture(solver, statusText, solver.BestObjectiveBound, solver.WallTime(),
                solver.NumConflicts(), solver.NumBranches()));
        } else {
            string? solutionInfo = solver.Response?.SolutionInfo;
            string? solveLog = solver.Response?.SolveLog;
            if (!string.IsNullOrWhiteSpace(solveLog)) {
                string? keyLine = solveLog.Split('\n')
                    .FirstOrDefault(line => line.Contains("invalid", StringComparison.OrdinalIgnoreCase)
                        || line.Contains("overflow", StringComparison.OrdinalIgnoreCase));
                if (keyLine?.Length > 2_000) keyLine = keyLine[..2_000];
                string tail = solveLog.Length > 4_000 ? solveLog[^4_000..] : solveLog;
                solveLog = string.IsNullOrWhiteSpace(keyLine) ? tail : $"{keyLine}\n...\n{tail}";
            }
            string failureDetail = string.IsNullOrWhiteSpace(solutionInfo)
                ? solver.ResponseStats()
                : $"{solutionInfo} | {solver.ResponseStats()}";
            if (!string.IsNullOrWhiteSpace(solveLog))
                failureDetail += $" | log={solveLog}";
            Write(new ExtremeSolverOutputDto(
                ExtremeRouteSolverProtocol.Version, statusText, input.MaxRoutes, 0,
                (long)Math.Round(solver.BestObjectiveBound), 1, solver.WallTime(),
                solver.NumConflicts(), solver.NumBranches(), [],
                input.WorkerCount, input.MemoryLimitMb,
                failureDetail));
        }
    }

    private ExtremeSolverOutputDto Capture(
        CpSolver solver,
        string status,
        double bestBound,
        double wallTime,
        long conflicts,
        long branches) {
        var routes = new List<ExtremeSolverRouteDto>();
        for (int r = 0; r < routeUsed.Length && solver.Value(routeUsed[r]) != 0; r++) {
            string? start = null;
            if (solver.Value(hasPickup[r]) != 0) {
                for (int w = 0; w < input.Warehouses.Count; w++)
                    if (solver.Value(startChoice[r, w]) != 0) { start = input.Warehouses[w].WarehouseId; break; }
            }
            string end = input.Warehouses[0].WarehouseId;
            for (int w = 0; w < input.Warehouses.Count; w++)
                if (solver.Value(endChoice[r, w]) != 0) { end = input.Warehouses[w].WarehouseId; break; }

            var picked = new List<ExtremeSolverPickupDto>();
            if (start is not null) {
                int w = warehouseIndex[start];
                for (int item = 0; item < input.Items.Count; item++) {
                    long quantity = solver.Value(pickup[r, w, item]);
                    if (quantity > 0)
                        picked.Add(new ExtremeSolverPickupDto(input.Items[item].ItemId, checked((int)quantity)));
                }
            }
            var tasks = new List<int>();
            for (int p = 0; p < input.Tasks.Count && solver.Value(slotUsed[r, p]) != 0; p++)
                for (int t = 0; t < input.Tasks.Count; t++)
                    if (solver.Value(taskAt[r, p, t]) != 0) { tasks.Add(t); break; }
            routes.Add(new ExtremeSolverRouteDto(r + 1, start, end, picked, tasks));
        }

        long distance = solver.Value(totalDistance);
        long bound = Math.Max(0, (long)Math.Floor(bestBound));
        double gap = distance <= 0 ? 0 : Math.Max(0, (distance - Math.Min(distance, bound)) / (double)distance);
        return new ExtremeSolverOutputDto(
            ExtremeRouteSolverProtocol.Version, status, input.MaxRoutes, distance, bound, gap,
            wallTime, conflicts, branches, routes, input.WorkerCount, input.MemoryLimitMb);
    }

    private void Write(ExtremeSolverOutputDto value) {
        string temporary = outputPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(value, jsonOptions));
        File.Move(temporary, outputPath, true);
    }
}
