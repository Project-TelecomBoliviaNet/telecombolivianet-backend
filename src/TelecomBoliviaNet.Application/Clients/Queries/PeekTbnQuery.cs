using MediatR;
using TelecomBoliviaNet.Application.Interfaces;

namespace TelecomBoliviaNet.Application.Clients.Queries;

public record PeekTbnQuery : IRequest<string>;

public class PeekTbnHandler : IRequestHandler<PeekTbnQuery, string>
{
    private readonly ITbnService _tbn;
    public PeekTbnHandler(ITbnService tbn) => _tbn = tbn;
    public Task<string> Handle(PeekTbnQuery _, CancellationToken ct) => _tbn.PeekNextAsync();
}
