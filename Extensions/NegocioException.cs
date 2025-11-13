using System;
using System.Runtime.Serialization;

namespace app_tramites.Extensions;

/// <summary>
/// Excepción personalizada para errores de negocio.
/// </summary>
[Serializable]
public class NegocioException : Exception
{
    /// <summary>
    /// Código opcional para clasificar la excepción de negocio.
    /// </summary>
    public string? ErrorCode { get; }

    public NegocioException()
    {
    }

    public NegocioException(string message)
        : base(message)
    {
    }

    public NegocioException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public NegocioException(string message, string errorCode)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public NegocioException(string message, string errorCode, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }

    protected NegocioException(SerializationInfo info, StreamingContext context)
        : base(info, context)
    {
        ErrorCode = info.GetString(nameof(ErrorCode));
    }

    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        if (info is null) throw new ArgumentNullException(nameof(info));
        info.AddValue(nameof(ErrorCode), ErrorCode);
        base.GetObjectData(info, context);
    }
}