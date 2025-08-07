using TelecomBoliviaNet.Application.DTOs.Payments;
using TelecomBoliviaNet.Domain.Primitives;

namespace TelecomBoliviaNet.Application.Interfaces;

public interface ICashCloseService
{
    Task<CashCloseDto>           GetOrCreateActiveTurnoAsync(Guid userId, string userName);
    Task<Result<CashCloseDto>>   CerrarTurnoAsync(Guid userId, string userName, string ip);
    Task<List<CashCloseDto>>     GetCashClosesAsync(Guid? userId, DateTime? desde, DateTime? hasta);
}
