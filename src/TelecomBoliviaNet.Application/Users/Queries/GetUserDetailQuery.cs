using MediatR;
using Microsoft.EntityFrameworkCore;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Users.Queries;

public record GetUserDetailQuery(Guid Id) : IRequest<UserSystemDetailDto?>;

public class GetUserDetailHandler : IRequestHandler<GetUserDetailQuery, UserSystemDetailDto?>
{
    private readonly IGenericRepository<UserSystem> _repo;

    public GetUserDetailHandler(IGenericRepository<UserSystem> repo) => _repo = repo;

    public async Task<UserSystemDetailDto?> Handle(GetUserDetailQuery q, CancellationToken ct)
    {
        var user = await _repo.GetAll()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == q.Id, ct);

        return user is null ? null : UserMapper.ToDetailDto(user);
    }
}
