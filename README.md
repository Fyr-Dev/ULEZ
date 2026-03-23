# ULEZ - Ultra Low Emission Zone

ULEZ adds district-based road charging to Cities: Skylines II. Turn ULEZ on for specific districts, charge private cars that travel inside those districts, and receive daily summaries about traffic and revenue. If CustomChirps is installed, the summary is posted to Chirper automatically.

## Source Code

This repository contains the source code for the published mod.

- GitHub is the canonical source for code and issue tracking
- Paradox Mods and Skyve are the distribution channels for players
- The published mod metadata can link back to this repository through the `ExternalLink` field in `Properties/PublishConfiguration.xml`
- Repository: `https://github.com/Fyr-Dev/ULEZ`

## What It Does

- Lets you enable ULEZ district by district
- Charges private cars travelling inside active ULEZ districts
- Charges each vehicle at most once per in-game day
- Tracks district traffic before and after ULEZ is enabled
- Shows live district and citywide ULEZ stats in the custom district panel
- Reports district trends over time through daily summaries

## How To Use

1. Create a district.
2. Select the district.
3. Leave ULEZ off for a while if you want a true pre-ULEZ traffic baseline for that district.
4. Enable ULEZ in the custom ULEZ panel when you are ready.
5. Let traffic run normally.
6. Review the district panel and the daily ULEZ report in the log or Chirper.

## What To Expect

ULEZ currently discourages driving by making it more expensive, not by forcing a direct pathfinding override.

- Drivers that keep using ULEZ districts pay recurring charges
- Over time, that makes private-car travel less attractive financially
- If you let a district run before enabling ULEZ, the mod can compare later traffic against that district's own pre-ULEZ baseline
- The district panel shows live traffic, charges, revenue, and citywide totals without waiting for the daily report
- The daily report shows whether private-car traffic in a district is rising, falling, or staying steady compared with its pre-ULEZ baseline when available
- The report also shows how much traffic and revenue the district and the whole ULEZ system have generated over time

## Settings

All settings are available in Options under ULEZ.

| Setting | Default | Range | Purpose |
|---------|---------|-------|---------|
| Enable ULEZ System | On | On/Off | Master toggle for the mod |
| ULEZ Daily Charge | 50 | 5-500 | Base charge applied to each charged vehicle |
| Charge Multiplier | 3.0x | 1.0-10.0x | Multiplies the base charge |
| Vehicle Scan Budget | 400 | 50-2000 | Maximum vehicles processed in one scan |
| Debug Logging | Off | On/Off | Enables extra troubleshooting logs |

## Daily Reports

Daily reports are meant to help you judge whether ULEZ is working.

- Daily money collected
- Number of observed private-car trips and number of chargeable trips
- Most active ULEZ district that day
- Whether private-car traffic in that district is up, down, or stable compared with its pre-ULEZ baseline when available
- Lifetime traffic, money, and charges generated in that district and across the full ULEZ system

## District Panel

When you select a district, the custom ULEZ panel shows:

- Whether ULEZ is active in that district
- Today, last full day, and lifetime traffic for that district
- Today, last full day, and lifetime revenue and charges for that district
- A color-coded trend note showing whether traffic is down, up, or broadly steady versus baseline
- Citywide ULEZ totals so you can compare the district against the wider network

If a district has not been watched before ULEZ is enabled, it will still work normally, but the trend note stays neutral until enough baseline data exists.

## Performance

- Vehicles are processed in batches instead of all at once
- Lane and edge lookups are cached
- Each vehicle is charged at most once per day

If your city is very large and you want smoother frame times, lower Vehicle Scan Budget. If you want charges to propagate faster and performance is already stable, raise it.

## Tips

- Start with one central district first
- If you want the clearest comparison, watch a district for at least one full in-game day before enabling ULEZ there
- Use a moderate base charge before raising the multiplier
- If charges feel delayed in a large city, increase Vehicle Scan Budget gradually
- Turn on Debug Logging only when you need to inspect behavior or diagnose another mod interaction

## License

MIT
