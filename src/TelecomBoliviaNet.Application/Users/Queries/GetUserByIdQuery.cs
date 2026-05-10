using MediatR;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Users.Queries;

public record GetUserByIdQuery(Guid Id) : IRequest<UserSystemDto?>;

public class GetUserByIdHandler : IRequestHandler<GetUserByIdQuery, UserSystemDto?>
{
    private readonly IGenericRepository<UserSystem> _repo;

    public GetUserByIdHandler(IGenericRepository<UserSystem> repo) => _repo = repo;

    public async Task<UserSystemDto?> Handle(GetUserByIdQuery q, CancellationToken ct)
    {
        var user = await _repo.GetByIdAsync(q.Id);
        return user is null ? null : UserMapper.ToDto(user);
    }
}
