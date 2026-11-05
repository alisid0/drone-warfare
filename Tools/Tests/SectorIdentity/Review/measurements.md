# Measured plan comparison

All pairs are **before -> after**. Renderer and build-time limits are explained in [the handoff](../README.md).

| Sector / seed | Buildings, whole sector | Buildings at planned sites | Scenery renderers, all LODs | Central field survey % | Whole-map parcel area % | Harness build ms |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| DeepForest / 1037 | 0 -> 1 | 0 -> 1 | 25,863 -> 34,746 | 5.1 -> 1.7 | 2.4 -> 0.9 | 1249 -> 1405 |
| IndustrialZone / 1148 | 74 -> 16 | 4 -> 9 | 14,300 -> 17,429 | 12.1 -> 8.4 | 15.7 -> 15.8 | 636 -> 709 |
| Airfield / 1222 | 4 -> 33 | 1 -> 6 | 14,935 -> 17,916 | 68.2 -> 68.3 | 56.7 -> 53.6 | 717 -> 701 |
| RailJunction / 1185 | 70 -> 15 | 1 -> 3 | 26,251 -> 27,435 | 57.2 -> 59.8 | 38.9 -> 40.7 | 744 -> 931 |
| TownOutskirts / 1111 | 33 -> 72 | 0 -> 38 | 15,301 -> 15,622 | 51.7 -> 53.9 | 30.1 -> 30.4 | 651 -> 671 |
| Marshland / 1074 | 29 -> 52 | 0 -> 0 | 26,578 -> 26,543 | 8.2 -> 11.9 | 12.5 -> 12.6 | 475 -> 487 |
| MarketTown / 1629 | 80 -> 41 | 0 -> 38 | 15,883 -> 19,977 | 34.6 -> 35.8 | 26.8 -> 27.6 | 611 -> 568 |

DeepForest nominal canopy at the same reserved location: 8.2% -> 73.1%. The candidate planted 6,049 core trees; the complete sector stayed within 11,500 trees.

These CPU timings exclude native terrain generation, Unity object creation, imports/uploads and GPU work. They do not certify the 15-second game build gate.
