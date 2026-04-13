using app_tramites.Models.ModelAi;

namespace app_tramites.Models.ViewModel;

public class ViewPagedProcessCases
{
    public List<ProcessCase> Items { get; set; } = [];
    public List<string> AvailableProcesses { get; set; } = [];
    public int TotalCount { get; set; }
    public int EvaluatedCount { get; set; }
    public int PendingCount { get; set; }
    public int ProcessCount { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalPages { get; set; } = 1;
    public int WindowDays { get; set; }
    public string SearchTerm { get; set; } = string.Empty;
    public string StatusFilter { get; set; } = string.Empty;
    public string TypeFilter { get; set; } = string.Empty;
    public string ProcessFilter { get; set; } = string.Empty;
    public string PeriodFilter { get; set; } = string.Empty;

    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;
    public bool IsWindowed => WindowDays > 0;
    public int PageItemStart => TotalCount == 0 ? 0 : ((PageNumber - 1) * PageSize) + 1;
    public int PageItemEnd => TotalCount == 0 ? 0 : Math.Min(TotalCount, PageNumber * PageSize);
}
