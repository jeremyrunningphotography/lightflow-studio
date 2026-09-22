using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ListBox = System.Windows.Controls.ListBox;

namespace LightflowStudio;

public partial class MainWindow
{
    internal Action<System.Diagnostics.ProcessStartInfo> OpenJobOutputFolder { get; set; } =
        request => System.Diagnostics.Process.Start(request);

    private void JobOutputPath_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkContentElement { Tag: string path }) RevealJobOutput(path);
    }

    private void RevealJobOutput(string path)
    {
        try
        {
            if (JobOutputLocation.RevealRequest(path) is { } request) OpenJobOutputFolder(request);
            else ConfirmationDialog.Confirm(this, "Output unavailable", "This output file is not available",
                "The Export may not have finished, or its output may have moved or been removed.", path, "Close");
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or IOException
            or UnauthorizedAccessException or ArgumentException)
        {
            ConfirmationDialog.Confirm(this, "Could not open output folder", "The output folder could not be opened",
                exception.Message, path, "Close");
        }
    }

    internal void JobsClear_Click(object sender, RoutedEventArgs e)
    {
        if (JobIdFrom(sender) is not { } id || _compactJobsCards.FirstOrDefault(card => card.JobId == id)?.CanClear != true) return;
        _dismissedTerminalJobIds.Add(id);
        ApplyJobsPresentation(_exportScheduler.Jobs);
    }

    internal void JobsClearAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var card in _compactJobsCards.Where(card => card.CanClear))
            _dismissedTerminalJobIds.Add(card.JobId);
        ApplyJobsPresentation(_exportScheduler.Jobs);
    }

    private void ClearAllJobsHistory_Click(object sender, RoutedEventArgs e) =>
        RemoveFullJobs(_historyRecords.Where(item => item.CanRemove).ToArray());

    private void RemoveFullJobs(IReadOnlyList<JobsWorkspaceItem> candidates)
    {
        if (candidates.Count == 0 || candidates.Any(item => !item.CanRemove)) return;
        var ids = JobsWorkspacePresentation.BackingHistoryRecordIds(candidates);
        var records = _durableHistoryRecords.Where(record => ids.Contains(record.JobId)).ToArray();
        var legacy = records.Any(record => record.Plan.Items.Count != 1);
        var retained = candidates.Any(item => item.RemovalKind == JobRemovalKind.RetainedProvenance);
        var noun = candidates.Count == 1 ? "Job" : "Jobs";
        var detail = "Saved Export and file-operation Job records will be deleted. Session-only Jobs will be cleared. " +
            "Source media, outputs, active work, recovery state, and output identity are unchanged." +
            (legacy ? " Older Jobs saved together are removed as one complete group, including unselected rows in that group." : "") +
            (retained ? " Premiere handoff provenance is retained for safe reconciliation; those rows are cleared for this session and can return after restart." : "");
        if (!ConfirmationDialog.Confirm(this, $"Remove {noun}", $"Remove {candidates.Count} terminal {noun} from this Jobs view?",
                detail, null, $"Remove {noun}")) return;
        try
        {
            // Each existing store owns its atomic write. Preserve successful removals if a later store fails.
            _jobHistory.Remove(ids);
            _deletedFullJobsTerminalJobIds.UnionWith(candidates.Where(item => item.HistoryRecordId is not null).Select(item => item.JobId));
            var fileIds = candidates.Where(item => item.RemovalKind == JobRemovalKind.FileOperationHistory)
                .Select(item => item.JobId).ToHashSet();
            _fileOperationJobs.RemoveHistory(fileIds);
            _deletedFullJobsTerminalJobIds.UnionWith(candidates.Select(item => item.JobId));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            ConfirmationDialog.Confirm(this, "Could not remove all Jobs", "Some Job records could not be removed.",
                exception.Message + " Unremoved records remain available. Source media and outputs are unchanged.", null, "Close");
        }
        finally { RefreshHistory(); }
    }

    private void CancelJob(Guid id)
    {
        if (_fileOperationJobs.Jobs.Any(job => job.Intent.OperationId == id)) _fileOperationJobs.Cancel(id);
        else if (_premiereJobs?.Jobs.Any(job => job.JobId == id) == true) _premiereJobs.Cancel(id);
        else if (!_visualIndexJobs.Cancel(id)) _exportScheduler.Cancel(id);
    }

    internal void JobRow_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBoxItem row) return;
        // A context click on a selected row preserves the existing multi-selection.
        if (!row.IsSelected && ItemsControl.ItemsControlFromItemContainer(row) is ListBox list)
            list.SelectedItem = row.DataContext;
        row.Focus();
        e.Handled = true;
    }

    internal void JobRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not ListBoxItem row || row.ContextMenu is not { } menu) return;
        var full = row.DataContext is JobsWorkspaceItem;
        var id = row.DataContext switch { JobsWorkspaceItem item => item.JobId, JobCardPresentation card => card.JobId, _ => Guid.Empty };
        JobActionState? Current() => full
            ? _historyRecords.FirstOrDefault(item => item.JobId == id)?.Actions
            : _compactJobsCards.FirstOrDefault(card => card.JobId == id)?.Actions;
        var actions = Current();
        menu.Items.Clear();
        if (actions is null) { e.Handled = true; return; }
        void Add(string label, Func<JobActionState, bool> eligible, Action execute)
        {
            if (!eligible(actions)) return;
            var item = new MenuItem { Header = label, Style = (Style)FindResource("LightflowMenuItemStyle") };
            item.Click += (_, _) => { if (Current() is { } current && eligible(current)) execute(); };
            menu.Items.Add(item);
        }
        Add("Pause", state => state.CanPause, () => _exportScheduler.Pause(id));
        Add("Resume", state => state.CanResume, () => _exportScheduler.Resume(id));
        Add("Retry", state => state.CanRetry, () => RetryVisualIndexOrExport(id));
        Add("Review & Rerun…", state => state.CanReviewAndRerun,
            () => { if ((_historyRecords.FirstOrDefault(item => item.JobId == id)?.HistoryRecord
                    ?? _durableHistoryRecords.FirstOrDefault(record => record.JobId == id)) is { } record) ReviewAndRerun(record); });
        Add("Cancel…", state => state.CanCancel, () => JobsCancel_Click(new MenuItem { Tag = id }, new RoutedEventArgs()));
        Add(full ? "Remove Job…" : "Clear", state => state.CanClear, () =>
        {
            if (full)
            {
                if (_historyRecords.FirstOrDefault(item => item.JobId == id) is { } item) RemoveFullJobs([item]);
            }
            else
            {
                _dismissedTerminalJobIds.Add(id);
                ApplyJobsPresentation(_exportScheduler.Jobs);
            }
        });
        if (menu.Items.Count == 0) e.Handled = true;
    }
}
