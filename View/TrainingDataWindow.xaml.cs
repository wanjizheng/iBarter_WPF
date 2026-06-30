using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace iBarter.View {
    /// <summary>
    /// OCR training-data labeler. Walks the iBarter Resources\ folder for the
    /// Phase F / Phase G _sharp.bmp files (the ones already preprocessed by
    /// the same Magick pipeline Tesseract will see at inference time) and lets
    /// the operator type the correct digit label for each one. The label is
    /// saved side-by-side as &lt;file&gt;.gt.txt so that Tesseract's
    /// tesseract.exe / tesstrain can pick them up directly.
    ///
    /// Workflow:
    ///   1. Run a few OCR scans so bin\...\Resources\ocr*_sharp.bmp exist.
    ///   2. Open this window. Files already labeled (have .gt.txt) are marked
    ///      with [+] in the list; unlabeled with [ ].
    ///   3. Type the correct value, Enter to save &amp; advance.
    ///   4. Once &gt;= 30 labeled, click "Generate train.lstmf" then
    ///      "Copy tesstrain command" and run it in a Python venv with
    ///      tesstrain installed.
    /// </summary>
    public partial class TrainingDataWindow : Syncfusion.Windows.Shared.ChromelessWindow {
        // File row bound to ListBox.
        private class FileRow {
            public string FileName { get; set; } = "";
            public string FullPath { get; set; } = "";
            public string Status { get; set; } = "";   // "[+]" labeled, "[ ]" unlabeled, "[x]" saved just now
            public string Label { get; set; } = "";
        }

        private readonly List<FileRow> _rows = new List<FileRow>();
        private int _currentIndex = -1;
        // Minimum recommended sample count before training gives useful
        // accuracy. Tesseract LSTM trains faster on smaller char sets, but
        // 30 is the floor below which overfitting to one specific layout
        // becomes obvious.
        private const int MinSamples = 30;

        public TrainingDataWindow() {
            InitializeComponent();
            ReloadFiles();
        }

        private static string DefaultSourceDir() {
            return AppDomain.CurrentDomain.BaseDirectory + "Resources";
        }

        private void ReloadFiles() {
            _rows.Clear();
            _currentIndex = -1;
            string srcDir = DefaultSourceDir();
            TextBox_SourceDir.Text = srcDir;

            if (!Directory.Exists(srcDir)) {
                TextBlock_Status.Text = "Source folder not found: " + srcDir;
                return;
            }

            // Phase F / G output files: ocr_<id>_sharp.bmp (F) and
            // ocrf_<id>_sharp.bmp (G). Skip _diff.bmp (intermediate).
            // De-dup by id: if both ocr_10_sharp.bmp and ocrf_10_sharp.bmp
            // exist, prefer the F one (smaller, cropped, no icon body).
            var byId = new SortedDictionary<string, FileRow>();
            foreach (var path in Directory.EnumerateFiles(srcDir, "*_sharp.bmp")) {
                string fn = Path.GetFileName(path);
                Match m = Regex.Match(fn, @"^ocr(f)?_(\d+)_sharp\.bmp$");
                if (!m.Success) continue;
                string id = m.Groups[2].Value;
                string kind = m.Groups[1].Value; // "" for F, "f" for G
                // Prefer F (kind == "") over G (kind == "f") for the same id
                string key = id;
                if (byId.TryGetValue(key, out var existing)) {
                    // Existing was F (no prefix), new one is G -> skip new
                    if (existing.FileName.StartsWith("ocr_") && kind == "f") continue;
                    // Otherwise prefer the F one we just saw
                    if (kind == "") byId[key] = MakeRow(path, id);
                    continue;
                }
                byId[key] = MakeRow(path, id);
            }

            foreach (var kv in byId) _rows.Add(kv.Value);

            ListBox_Files.ItemsSource = _rows;
            TextBlock_FileCount.Text = _rows.Count.ToString();

            // Jump to the first unlabeled file.
            for (int i = 0; i < _rows.Count; i++) {
                if (_rows[i].Status == "[ ]") {
                    SelectIndex(i);
                    return;
                }
            }
            if (_rows.Count > 0) SelectIndex(0);
            UpdateProgress();
        }

        private FileRow MakeRow(string path, string id) {
            string gtPath = path + ".gt.txt";
            string label = "";
            string status = "[ ]";
            if (File.Exists(gtPath)) {
                try { label = File.ReadAllText(gtPath).Trim(); } catch { }
                if (!string.IsNullOrEmpty(label)) status = "[+]";
            }
            return new FileRow {
                FileName = Path.GetFileName(path),
                FullPath = path,
                Status = status,
                Label = label,
            };
        }

        private void SelectIndex(int idx) {
            if (idx < 0 || idx >= _rows.Count) return;
            _currentIndex = idx;
            ListBox_Files.SelectedIndex = idx;
            ListBox_Files.ScrollIntoView(_rows[idx]);
            ShowCurrent();
        }

        private void ShowCurrent() {
            if (_currentIndex < 0 || _currentIndex >= _rows.Count) {
                Image_Current.Source = null;
                TextBlock_CurrentFile.Text = "(none)";
                TextBlock_CurrentSize.Text = "";
                TextBox_Label.Text = "";
                return;
            }
            var row = _rows[_currentIndex];
            TextBlock_CurrentFile.Text = row.FileName;
            try {
                var fi = new FileInfo(row.FullPath);
                TextBlock_CurrentSize.Text = $"{fi.Length / 1024} KB";
            } catch { TextBlock_CurrentSize.Text = "?"; }

            try {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad; // release file handle
                bmp.UriSource = new Uri(row.FullPath);
                bmp.EndInit();
                Image_Current.Source = bmp;
            } catch (Exception ex) {
                TextBlock_Status.Text = "Image load failed: " + ex.Message;
            }

            TextBox_Label.Text = row.Label;
            TextBox_Label.Focus();
            TextBox_Label.SelectAll();
        }

        private void UpdateProgress() {
            int labeled = _rows.Count(r => r.Status != "[ ]");
            int total = _rows.Count;
            TextBlock_Progress.Text = $"{labeled} / {total} labeled";
            ProgressBar_Labeled.Maximum = Math.Max(1, total);
            ProgressBar_Labeled.Value = labeled;
            TextBlock_Threshold.Foreground = labeled >= MinSamples ? System.Windows.Media.Brushes.DarkGreen : System.Windows.Media.Brushes.DarkOrange;
            TextBlock_Threshold.Text = labeled >= MinSamples
                ? $"(ready to train - {labeled} >= {MinSamples})"
                : $"(need >= {MinSamples} to train, have {labeled})";
        }

        // ----- UI event handlers -----

        private void Button_Reload_Click(object sender, RoutedEventArgs e) {
            ReloadFiles();
            TextBlock_Status.Text = "Reloaded.";
        }

        private void ListBox_Files_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) {
            int idx = ListBox_Files.SelectedIndex;
            if (idx >= 0 && idx != _currentIndex) {
                _currentIndex = idx;
                ShowCurrent();
            }
        }

        private void TextBox_Label_KeyDown(object sender, KeyEventArgs e) {
            if (e.Key == Key.Enter) {
                Button_Save_Click(sender, e);
                e.Handled = true;
            }
        }

        private void Button_Save_Click(object sender, RoutedEventArgs e) {
            if (_currentIndex < 0) return;
            string raw = (TextBox_Label.Text ?? "").Trim();
            if (!Regex.IsMatch(raw, @"^[0-9]{1,4}$")) {
                TextBlock_Status.Text = "Label must be 1-4 digits (got: '" + raw + "').";
                return;
            }
            int n = int.Parse(raw);
            if (n <= 0 || n >= 10000) {
                TextBlock_Status.Text = "Label must be 1..9999.";
                return;
            }

            var row = _rows[_currentIndex];
            try {
                File.WriteAllText(row.FullPath + ".gt.txt", raw);
                row.Label = raw;
                row.Status = "[x]"; // saved this session
                // ListBox template will re-render via ToString refresh: nudge it
                var idx = ListBox_Files.SelectedIndex;
                ListBox_Files.Items.Refresh();
                ListBox_Files.SelectedIndex = idx;
                TextBlock_Status.Text = $"Saved {row.FileName} = {raw}.";
            }
            catch (Exception ex) {
                TextBlock_Status.Text = "Save failed: " + ex.Message;
                return;
            }

            UpdateProgress();
            // Auto-advance to next unlabeled file.
            for (int step = 1; step <= _rows.Count; step++) {
                int ni = (_currentIndex + step) % _rows.Count;
                if (_rows[ni].Status == "[ ]") {
                    SelectIndex(ni);
                    return;
                }
            }
            // No more unlabeled - just stay where we are.
        }

        private void Button_Skip_Click(object sender, RoutedEventArgs e) {
            if (_rows.Count == 0) return;
            int ni = (_currentIndex + 1) % _rows.Count;
            SelectIndex(ni);
            TextBlock_Status.Text = "Skipped.";
        }

        private void Button_Prev_Click(object sender, RoutedEventArgs e) {
            if (_rows.Count == 0) return;
            int pi = (_currentIndex - 1 + _rows.Count) % _rows.Count;
            SelectIndex(pi);
            TextBlock_Status.Text = "Previous.";
        }

        private void Button_Reveal_Click(object sender, RoutedEventArgs e) {
            try {
                System.Diagnostics.Process.Start("explorer.exe", DefaultSourceDir());
            } catch (Exception ex) {
                TextBlock_Status.Text = "Open folder failed: " + ex.Message;
            }
        }

        private void Button_Export_Click(object sender, RoutedEventArgs e) {
            // Collect every labeled row (Status != "[ ]") and write a
            // Tesstrain-compatible lstmf list + a copy of the BMPs to a
            // sibling train_data folder. tesseract.exe --train_from_text
            // expects <path>\tessdata\eng.training_files.txt listing each
            // <basename>.lstmf file, so we generate that too.
            string srcDir = DefaultSourceDir();
            var labeled = _rows.Where(r => r.Status != "[ ]").ToList();
            if (labeled.Count == 0) {
                TextBlock_ExportStatus.Text = "Nothing labeled yet.";
                return;
            }
            string outDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr_training");
            string lstmfDir = Path.Combine(outDir, "lstmf");
            string srcOutDir = Path.Combine(outDir, "src");
            try {
                Directory.CreateDirectory(lstmfDir);
                Directory.CreateDirectory(srcOutDir);
                var lstLines = new List<string>();
                foreach (var r in labeled) {
                    // Copy BMP to src/
                    string dst = Path.Combine(srcOutDir, r.FileName);
                    File.Copy(r.FullPath, dst, overwrite: true);
                    // Write .gt.txt side-by-side in src/ (Tesstrain expects
                    // it next to the image so it can auto-discover labels).
                    File.WriteAllText(dst + ".gt.txt", r.Label);
                    // Reference the basename without extension - Tesstrain
                    // will look for <name>.lstmf in the lstmf/ folder.
                    string basename = Path.GetFileNameWithoutExtension(r.FileName);
                    lstLines.Add(basename);
                }
                // Write the master list
                string listFile = Path.Combine(outDir, "eng.training_files.txt");
                File.WriteAllLines(listFile, lstLines);
                TextBlock_ExportStatus.Text =
                    $"Exported {labeled.Count} samples to {outDir}\n" +
                    $"Master list: {listFile}\n" +
                    $"Next: run tesstrain (see command below).";
                Clipboard.SetText(BuildTesstrainCommand(outDir));
            }
            catch (Exception ex) {
                TextBlock_ExportStatus.Text = "Export failed: " + ex.Message;
            }
        }

        private void Button_CopyCmd_Click(object sender, RoutedEventArgs e) {
            string cmd = BuildTesstrainCommand(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ocr_training"));
            try {
                Clipboard.SetText(cmd);
                TextBlock_ExportStatus.Text = "tesstrain command copied to clipboard.\n\n" + cmd;
            } catch (Exception ex) {
                TextBlock_ExportStatus.Text = "Clipboard failed: " + ex.Message + "\n\n" + cmd;
            }
        }

        private static string BuildTesstrainCommand(string trainDataDir) {
            // Tesstrain (https://github.com/tesseract-ocr/tesstrain) workflow:
            //   - generate .lstmf files from each <name>.png + <name>.gt.txt
            //   - train LSTM with `make training MODEL_NAME=bdo_digits`
            // We generate the .lstmf via tesseract.exe (ships with the BDO
            // tessdata folder's tesseract.exe - or any 5.x tesseract install).
            string lstmfDir = Path.Combine(trainDataDir, "lstmf");
            string srcDir = Path.Combine(trainDataDir, "src");
            return
                "# 1. Convert each src/<name>.bmp + <name>.gt.txt to src/<name>.lstmf\n" +
                "cd \"" + trainDataDir + "\"\n" +
                "for f in src/*.bmp; do\n" +
                "  base=\"${f%.bmp}\"\n" +
                "  tesseract \"$f\" \"${base%.bmp}\" -l eng --psm 7 lstm.train\n" +
                "done\n\n" +
                "# 2. Train via tesstrain (https://github.com/tesseract-ocr/tesstrain)\n" +
                "git clone https://github.com/tesseract-ocr/tesstrain\n" +
                "cd tesstrain\n" +
                "make training MODEL_NAME=bdo_digits \\\n" +
                "     TESSDATA=\"" + Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tessdata")) + "\" \\\n" +
                "     START_MODEL=eng_best \\\n" +
                "     EPOCHS=300 \\\n" +
                "     LANG_TYPE=indic \\\n" +
                "     DATA_DIR=\"" + Path.GetFullPath(trainDataDir) + "\" \\\n" +
                "     OUTPUT_DIR=\"" + Path.GetFullPath(Path.Combine(trainDataDir, "output")) + "\"\n\n" +
                "# 3. After training, copy output/bdo_digits.traineddata to tessdata/\n" +
                "#    then update iBarter GetTesseract() to load it instead of eng_best.\n";
        }
    }
}