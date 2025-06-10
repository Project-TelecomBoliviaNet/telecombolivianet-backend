namespace TelecomBoliviaNet.Application.Options;

/// <summary>
/// Horario laboral para el cálculo de SLA con schedule=Laboral.
/// Configurable desde appsettings.json sección "BusinessHours".
/// </summary>
public class BusinessHoursOptions
{
    public const string Section = "BusinessHours";

    /// <summary>Hora de inicio de jornada laboral (0-23). Default: 8.</summary>
    public int StartHour { get; set; } = 8;

    /// <summary>Hora de fin de jornada laboral (0-23). Default: 18.</summary>
    public int EndHour { get; set; } = 18;

    /// <summary>Offset UTC de la zona horaria de operación. Default: -4 (Bolivia).</summary>
    public int UtcOffsetHours { get; set; } = -4;
}
