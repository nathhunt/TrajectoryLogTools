# Set Compare tolerances

When tolerances are set, the Compare page marks each difference as within or over tolerance. Without
them, it shows the numbers only.

!!! warning "To be confirmed"
    The department's tolerances, if any, go here. The pictures use 1 mm, 0.5° and 0.5 MU as examples only.

1. Select **Settings** (the gear at the top right).

    ![Settings](../images/settings.png){ .shot }

2. Under **Compare tolerances** (3), type a number for each you want, or leave it empty:
    - **MLC actual, max |A − B| (mm)**
    - **Gantry actual, max |A − B| (°)**
    - **MU actual, max |A − B| (MU)**
3. Select **Save**.

Tolerances must be numbers greater than 0, with a full stop as the decimal point (for example `0.5`).

On the Compare page, each card then reads *Within 1 mm · …* in green or *Over 1 mm · …* in red. Each
tolerance is compared with the largest difference (max |A − B|) of the actual values.

![Cards with tolerances](../images/compare-cards.png){ .shot }
