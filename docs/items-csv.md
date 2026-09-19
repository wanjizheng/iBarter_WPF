# Items.csv

`docs` contains developer documentation only. It is excluded from the application build/publish assets and is never read at runtime. Do not deploy it. The runtime catalog is `Resources/Items.csv`; the compatibility fallback is embedded from that same CSV during compilation.

All planning weights are maintained in `Resources/Items.csv`, in the fifth column:

`Name,ID,LV,Number,WeightLT`

The file has no header. `WeightLT` is the weight of **one item**, in LT, using a decimal point and up to two decimal places. It is not the weight of one exchange.

Examples:

```csv
Fig,7018,0,-1,0.10
Knitting Yarn,5854,0,50,0.10
Brass Ingot,4066,0,10,0.30
```

Keep the existing first four columns when adjusting weight. Restart iBarter after editing the installed CSV, then regenerate affected routes. Ordinary, TAG, manual cargo and smart search all use these values. Non-cargo reward rows retain their existing zero planning weight.

Legacy four-column files remain readable. Their missing weights fall back to the copy of this same CSV bundled at build time. An explicit fifth-column value always takes priority, including zero. No separate JSON or per-item C# weight list is used.

The initial land-material weights were checked on 2026-09-17 against BDO Codex: `https://bdocodex.com/us/item/<ID>/`, for example [Fig](https://bdocodex.com/us/item/7018/) and [Knitting Yarn](https://bdocodex.com/us/item/5854/).
