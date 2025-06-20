using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using TelecomBoliviaNet.Application.DTOs.Auth;
using TelecomBoliviaNet.Application.DTOs.Common;
using TelecomBoliviaNet.Domain.Entities.Auth;
using TelecomBoliviaNet.Domain.Interfaces;

namespace TelecomBoliviaNet.Application.Users.Queries;

public record GetUsersListQuery(
    int     Page       = 1,
    int     PageSize   = 20,
    string? RoleFilter = null,
    string? Search     = null) : IRequest<PagedResult<UserSystemDto>>;

public class GetUsersListHandler : IRequestHandler<GetUsersListQuery, PagedResult<UserSystemDto>>
{
    private readonly IGenericRepository<UserSystem> _repo;
    private readonly string                         _botEmail;

    public GetUsersListHandler(
        IGenericRepository<UserSystem> repo,
        IConfiguration                 config)
    {
        _repo     = repo;
        _botEmail = (config["SISTEMA_BOT_EMAIL"] ?? "bot@telecombolivianet.bo").ToLower();
    }

    public async Task<PagedResult<UserSystemDto>> Handle(
        GetUsersListQuery q, CancellationToken ct)
    {
        var query = _repo.GetAll().Where(u => u.Email != _botEmail);

        if (!string.IsNullOrWhiteSpace(q.RoleFilter) &&
            Enum.TryParse<UserRole>(q.RoleFilter, out var role))
            query = query.Where(u => u.Role == role);

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var s = q.Search.ToLower();
            query = query.Where(u =>
                u.FullName.ToLower().Contains(s) ||
                u.Email.ToLower().Contains(s));
        }

        query = query.OrderBy(u => u.FullName);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((q.Page - 1) * q.PageSize)
            .Take(q.PageSize)
            .ToListAsync(ct);

        return new PagedResult<UserSystemDto>(items.Select(UserMapper.ToDto), total, q.Page, q.PageSize);
    }
}
