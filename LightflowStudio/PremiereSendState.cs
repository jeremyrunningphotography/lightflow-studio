using System.IO;

namespace LightflowStudio;

internal enum PremiereSendRoute { Settings, Disconnected, Send }

/// <summary>Live project/bin choices are transient; durable handoff identity remains in the Catalog.</summary>
internal sealed class PremiereSendState
{
    public string? DestinationId { get; private set; }
    public IReadOnlyList<PremiereBin> Bins { get; private set; } = [];
    public string? SelectedBinId { get; set; }
    public string Message { get; private set; } = "";
    public static PremiereConnection WithInstallation(PremiereConnection live, PremiereConnection installation) =>
        live.State == PremiereConnectionState.Ready ? installation : live;
    public static PremiereSendRoute Route(PremiereConnection connection) => connection.State switch
    {
        PremiereConnectionState.Connected => PremiereSendRoute.Send,
        PremiereConnectionState.PremiereNotInstalled or PremiereConnectionState.CompanionNotInstalled
            or PremiereConnectionState.UpdateRequired => PremiereSendRoute.Settings,
        _ => PremiereSendRoute.Disconnected
    };

    public bool Refresh(PremiereConnection connection)
    {
        var project = connection.State == PremiereConnectionState.Connected ? connection.Companion?.Project : null;
        var destination = project is not null && Path.IsPathFullyQualified(project.Path)
            ? PremiereProtocol.DestinationId(project) : null;
        var changed = destination != DestinationId;
        if (changed) SelectedBinId = null;
        DestinationId = destination;
        Bins = destination is null ? [] : connection.Companion!.Bins;
        if (!Bins.Any(bin => bin.Id == SelectedBinId)) SelectedBinId = null;
        Message = connection.State != PremiereConnectionState.Connected ? "The companion is disconnected. Reconnect through integration Settings."
            : destination is null ? "Open and save a Premiere project to choose a destination."
            : Bins.Count == 0 ? "Waiting for bins from the active project. If this persists, check the companion for an enumeration error."
            : changed ? "Choose a destination bin in the active project." : "";
        return changed;
    }

    public bool CanSend(PremiereConnection connection) => DestinationId is not null
        && connection.State == PremiereConnectionState.Connected
        && connection.Companion?.Project is { } project && Path.IsPathFullyQualified(project.Path)
        && PremiereProtocol.DestinationId(project) == DestinationId
        && SelectedBinId is not null && connection.Companion.Bins.Any(bin => bin.Id == SelectedBinId);
}
