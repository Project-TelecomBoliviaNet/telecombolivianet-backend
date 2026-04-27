namespace TelecomBoliviaNet.Application.Interfaces;

/// <summary>
/// Contrato para la generación de códigos TBN correlativos.
/// La implementación está en Infrastructure para acceder a AppDbContext.
/// </summary>
public interface ITbnService
{
    /// <summary>Genera el próximo código TBN de forma atómica (ej: TBN-0042).</summary>
    Task<string> GenerateNextAsync();

    /// <summary>Devuelve el código que se generaría a continuación (solo lectura).</summary>
    Task<string> PeekNextAsync();
}
