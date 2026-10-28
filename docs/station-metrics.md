# Station metrics: design notes

The current panel shows lifetime boardings and rolling daily average boardings. Both directions are already stored; expanding the panel is still a design proposal.

Proposed compact table:

| | Boardings | Alightings |
|---|---:|---:|
| Daily average | 62.0 | 59.0 |
| Total | 1,240 | 1,180 |

Both averages should use the same up-to-seven-complete-days window, exclude the first partial tracking day and the current day, and include zero-traffic days. Existing daily exit buckets can supply alighting averages without resetting history.

Boardings plus alightings can describe station activity. Summing that activity across the network counts both ends of each journey; it must not be presented as the number of journeys. A repeated commuter can board more than once, so boardings are not unique beavers. Same-station excursions remain possible.

For time-of-day patterns, use the native attraction **Popularity** strip as the visual reference: small hourly bars and a current-hour marker. An adapted station strip could show combined activity by hour with separate boarding/alighting numbers on hover. Label it **Activity by hour**, not occupancy percentage: a station has no fixed seat capacity comparable to an agora or medical bed.

Hourly data is not collected by the current saved counter. A future change should keep fixed hourly bins for a bounded day window, with explicit handling of pre-upgrade saves that have daily totals but no hourly history. It does not require retaining passenger routes or high-resolution events.

Origin–destination aggregates would answer which station pairs carry traffic, but they are a separate feature. Daily totals alone cannot establish journey purposes, return routes or hauling patterns.
