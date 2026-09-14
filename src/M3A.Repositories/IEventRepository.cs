using M3A.Domain.Entities;

namespace M3A.Repositories;

/// <summary>Data access for the <see cref="Event"/> aggregate root.</summary>
public interface IEventRepository : IRepository<Event>;
