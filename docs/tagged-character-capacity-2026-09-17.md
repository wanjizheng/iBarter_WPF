# Character receiving limits

The reported Lema transfer put two LV5 Blue Quartz items on a TAG character already carrying five LV5 Golden Herbs: 120 + 5,000 + 2,000 = 7,120 LT. The saved character capacity is 3,213 LT; its configured 170% threshold is 5,462.1 LT.

Two defects allowed this: LV5 was classified as stackable, and individual-item receives checked only the weight before receiving. LV5–LV7 now occupy one slot per item. Their transfers require quantity one and enough weight headroom after receiving, including occupied LT. The same receive validation applies to both characters and mounts, including temporary receiving space for sale macros. Real stacks retain the existing single-stack receive exception. Compiler allocations use floor rather than ceiling so they cannot reserve an extra item across the threshold.

The NA/EU inventory guide documents the 170% general limit: https://www.naeu.playblackdesert.com/en-US/Wiki?wikiNo=13 . The post-receive check intentionally uses that configured value as a conservative ceiling for individual goods; it does not claim live-game verification of a possible final-item boundary exception. The settings label and explanation distinguish individual goods from true stacks.

Old plans with illegal transfers fail replay validation and are hidden with a replan message. They are not grandfathered into the new simulator. If real initial cargo prevents the ordinary baseline from starting, a fallback can sail to the home warehouse, legally unload/sell carried goods, and then establish a feasible plan; every operation remains in the verified route. No cargo is teleported into warehouse stock.

Validation:

- 420 route tests passed. Added coverage for the exact 5+2 case on both characters, LV5–LV7 slots, true stacks, the exact receiving boundary, and configurable percentages.
- WPF build and UI smoke passed; existing build warnings remain.
- Current runtime snapshot: old illegal plan rejected, remaining three trades replanned and saved/restored successfully.
- Preserved seven-trade snapshot: two legal individual character receives, 42.94 km.
- Preserved eight-trade snapshot with 7,120 LT peak: seven legal individual character receives, all exchanges completed, 40.84 km. Every non-stackable character receive was checked against the destination capacity and configured ratio.
- All outputs are isolated under `Tools/TaggedTransportUiSmoke/bin/character-weight-fix/`. Installed executable and user runtime Resources were not modified. No in-game inventory was operated.
