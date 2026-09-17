# TAG departure handling

TAG candidates now rank total sailing distance first, then overloaded sailing distance, route count and operation count. This does not introduce an elapsed-time objective or change ordinary planning. The overloaded-distance total is derived during simulator replay, including restored plans; it is not an extra persisted input that invalidates older saves.

The voyage compiler compares departure-port and arrival-port handling using the same distance priorities. Auto Plan also examines existing overloaded legs and tries moving a prefix of the next port's personal cargo handling to the departure port. It requires both enabled ports, excludes warehouse transfers, preserves island/exchange order, and replays every operation before accepting an improvement. Changed character availability, capacity, slots or mount access can therefore reject a move without discarding the original valid candidate.

On the supplied runtime session, Midnight to Grandiha previously departed at 28,411 LT against a 26,410 LT ship limit. Moving the existing handling to Midnight reduces that departure to 20,411 LT. Total distance remains 66.92 km; overloaded distance drops from 16.58 km to zero. Final inventories and exchange order are unchanged. This is simulator validation against configured port capabilities, not a new live-game verification.

Validation: 400 routing tests pass, including fresh voyage compilation, saved-incumbent improvement, disabled departure ports and distance priority. The actual-save audit also verifies full replay after Auto Plan. Runtime files are read-only during the audit; rebuilding the application and running Auto Plan applies this behavior to an existing save.
