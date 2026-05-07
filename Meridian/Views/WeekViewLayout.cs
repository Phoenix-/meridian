using Meridian.Models;

namespace Meridian.Views;

public record EventLayout(
    CalendarEvent Event,
    double Top,
    double Left,
    double Width,
    double Height,
    int ZIndex
);

public static class WeekViewLayout
{
    public const double HourHeight = 60.0;
    public const double MinEventHeight = 22.0;
    private const double ContainedIndent = 12.0; // left indent for events contained within another

    public static List<EventLayout> LayoutDay(IEnumerable<CalendarEvent> events, double columnWidth)
    {
        var sorted = events
            .Where(e => !e.IsAllDay)
            .OrderBy(e => e.Start)
            .ThenByDescending(e => e.End)
            .ToList();

        if (sorted.Count == 0) return [];

        int n = sorted.Count;

        // Step 1: find "container" for each event — the longest event that fully contains it
        var container = new int[n];
        Array.Fill(container, -1);
        for (int i = 0; i < n; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (i == j) continue;
                // j contains i if j started before or at i and ends after or at i
                if (sorted[j].Start <= sorted[i].Start && sorted[j].End >= sorted[i].End && j != i)
                {
                    // Pick the tightest container (shortest duration that still contains i)
                    if (container[i] == -1)
                        container[i] = j;
                    else
                    {
                        var curDur = sorted[container[i]].End - sorted[container[i]].Start;
                        var newDur = sorted[j].End - sorted[j].Start;
                        if (newDur < curDur) container[i] = j;
                    }
                }
            }
        }

        // Step 2: among "top-level" events (no container), assign slots by overlap
        var topLevel = Enumerable.Range(0, n).Where(i => container[i] == -1).ToList();
        var col = new int[n];
        Array.Fill(col, 0);

        var slotEnds = new List<DateTime>();
        foreach (int i in topLevel)
        {
            var occupied = new HashSet<int>();
            foreach (int j in topLevel)
                if (j != i && sorted[j].End > sorted[i].Start && col[j] >= 0)
                    occupied.Add(col[j]);
            // only already-assigned top-level events
            var occupiedAssigned = new HashSet<int>();
            foreach (int j in topLevel)
            {
                if (j >= i) break; // only assigned so far
                if (sorted[j].End > sorted[i].Start)
                    occupiedAssigned.Add(col[j]);
            }
            int slot = 0;
            while (occupiedAssigned.Contains(slot)) slot++;
            col[i] = slot;
        }

        // Step 3: compute total slots for each top-level group
        // For each top-level event, find max concurrent slot
        var totalSlots = new int[n];
        foreach (int i in topLevel)
        {
            int maxSlot = col[i];
            foreach (int j in topLevel)
                if (j != i && sorted[j].Start < sorted[i].End && sorted[j].End > sorted[i].Start)
                    maxSlot = Math.Max(maxSlot, col[j]);
            totalSlots[i] = maxSlot + 1;
        }

        // Step 4: build layouts
        var result = new List<EventLayout>(n);

        foreach (int i in topLevel)
        {
            double slotW = (columnWidth - 2) / totalSlots[i];

            // Right edge: extend to nearest concurrent top-level event in higher slot
            int rightSlot = totalSlots[i];
            foreach (int j in topLevel)
            {
                if (j == i) continue;
                if (col[j] > col[i] && sorted[j].Start < sorted[i].End && sorted[j].End > sorted[i].Start)
                    rightSlot = Math.Min(rightSlot, col[j]);
            }

            double left = col[i] * slotW + 1;
            double right = rightSlot * slotW - 1;
            double top = TimeToY(sorted[i].Start.TimeOfDay);
            double bottom = TimeToY(sorted[i].End.TimeOfDay);
            if (bottom <= top) bottom = top + MinEventHeight;
            double height = Math.Max(bottom - top, MinEventHeight);

            result.Add(new EventLayout(sorted[i], top, left, Math.Max(right - left, 20), height, col[i]));
        }

        // Step 5: layout contained events — cascade inside their container's right portion
        var contained = Enumerable.Range(0, n).Where(i => container[i] != -1).ToList();

        // Group contained events by their root container, then layout recursively within right portion
        foreach (int i in contained)
        {
            int c = container[i];
            // find the container's layout to get its right edge
            var cLayout = result.FirstOrDefault(r => r.Event == sorted[c]);
            double areaLeft = cLayout != null ? cLayout.Left + ContainedIndent : ContainedIndent;
            double areaWidth = columnWidth - areaLeft - 2;

            // Among contained events sharing the same container, assign slots
            var siblings = contained.Where(j => container[j] == c).ToList();
            var sibCol = new int[n];
            var sibSlotEnds = new List<DateTime>();
            foreach (int s in siblings.OrderBy(s => sorted[s].Start))
            {
                var occ = new HashSet<int>();
                foreach (int s2 in siblings)
                    if (s2 < s && sorted[s2].End > sorted[s].Start)
                        occ.Add(sibCol[s2]);
                int slot = 0;
                while (occ.Contains(slot)) slot++;
                sibCol[s] = slot;
                if (slot >= sibSlotEnds.Count) sibSlotEnds.Add(sorted[s].End);
                else sibSlotEnds[slot] = sorted[s].End;
            }

            int sibTotalSlots = siblings.Select(s => sibCol[s]).Max() + 1;
            double sibSlotW = areaWidth / sibTotalSlots;

            foreach (int s in siblings)
            {
                // extend right to next sibling slot
                int rightSlot = sibTotalSlots;
                foreach (int s2 in siblings)
                    if (sibCol[s2] > sibCol[s] && sorted[s2].Start < sorted[s].End && sorted[s2].End > sorted[s].Start)
                        rightSlot = Math.Min(rightSlot, sibCol[s2]);

                double left = areaLeft + sibCol[s] * sibSlotW;
                double right = areaLeft + rightSlot * sibSlotW - 1;
                double top = TimeToY(sorted[s].Start.TimeOfDay);
                double bottom = TimeToY(sorted[s].End.TimeOfDay);
                if (bottom <= top) bottom = top + MinEventHeight;
                double height = Math.Max(bottom - top, MinEventHeight);

                result.Add(new EventLayout(sorted[s], top, left, Math.Max(right - left, 20), height, sibCol[s] + 1));
            }
        }

        return result;
    }

    public static double TimeToY(TimeSpan time) => time.TotalHours * HourHeight;
}
