using System.IO;
using Lightflow.Application;
namespace LightflowStudio;
public sealed class CatalogLocationAdmissionException(string diagnostic, StorageLocationReason reason) : IOException(diagnostic)
{
    internal StorageLocationReason Reason { get; } = reason;
}
