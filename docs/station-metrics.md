# Station metrics

Implemented in development v0.1.11:

| | Boardings | Alightings |
|---|---:|---:|
| Daily average | 62.0 | 59.0 |
| Total | 1,240 | 1,180 |

Both averages use the same up-to-seven-complete-days window, exclude the first partial tracking day and current day, and include zero-traffic days. Existing saved daily exit buckets supply alighting averages without resetting history.

Boardings plus alightings describe station activity. Summing activity across the network counts both ends of each journey; it is not the number of journeys or unique beavers. Same-station excursions remain valid.

## Hourly strip

The UI loads the game's actual `Game/AttractionLoadRate` bar template, with its native textures, dimensions and current-hour marker. The hourly strip is labeled **Activity by hour**. Bars show combined activity, normalized to that station's busiest hour; hover shows boardings and alightings separately, the hour interval, and the observation window. A station has no seating capacity, so the strip never presents a percent-full claim.

Hourly coverage has its own start day/progress. Initially the strip shows today's observations, including a current partial hour. Future hours and hours before first tracking are explicitly unobserved in tooltips. Once a complete hourly-history day exists, the strip averages up to seven complete days, including quiet days. It labels that period separately from the daily table because an upgraded save can have mature daily history but newly started hourly coverage.

History is fixed-size per station: at most eight daily sets, each containing two arrays of 24 Int64 counts. Capture and restore copy arrays rather than share mutable state. The save migration reads schema one and writes schema two, preserving lifetime/daily counts without manufacturing past hourly data. The separate replay recorder retains its full bounded capture window for audit arithmetic.

Origin–destination aggregates remain a separate future feature. Counts alone do not establish trip purpose, return route or hauling patterns.
