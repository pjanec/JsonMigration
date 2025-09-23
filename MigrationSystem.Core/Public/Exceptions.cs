using MigrationSystem.Core.Public.DataContracts;
using Newtonsoft.Json.Linq;
using System;

namespace MigrationSystem.Core.Public.Exceptions;

/// <summary>
/// Thrown by the IDataApi when an in-memory data migration fails and the data
/// must be quarantined by the calling application.
/// </summary>
public class MigrationQuarantineException : Exception
{
    public QuarantineRecord QuarantineRecord { get; }
    public JObject OriginalData { get; }
    public MetaBlock OriginalMetadata { get; }

    public MigrationQuarantineException(
        string message,
        QuarantineRecord quarantineRecord,
        JObject originalData,
        MetaBlock originalMetadata) : base(message)
    {
        QuarantineRecord = quarantineRecord;
        OriginalData = originalData;
        OriginalMetadata = originalMetadata;
    }
}