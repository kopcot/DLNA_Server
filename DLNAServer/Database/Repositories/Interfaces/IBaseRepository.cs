using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Query;
using System.Linq.Expressions;

namespace DLNAServer.Database.Repositories.Interfaces
{
    public interface IBaseRepository<T>
    {
        DlnaDbContext DbContext { get; }
        Task<bool> AddAsync(T entity);
        Task<bool> AddRangeAsync(IEnumerable<T> entities);
        Task<T?> GetByIdAsync(Guid guid, bool asNoTracking = false, bool useCachedResult = true);
        Task<T?> GetByIdAsync(string guid, bool asNoTracking = false, bool useCachedResult = true);
        Task<ReadOnlyMemory<T>> GetAllAsync(bool withIncludes = true, bool asNoTracking = false, bool useCachedResult = true);
        Task<ReadOnlyMemory<T>> GetAllAsync(int skip, int take, bool withIncludes = true, bool asNoTracking = false, bool useCachedResult = true);
        Task<ReadOnlyMemory<T>> GetAllByIdsAsync(IEnumerable<Guid> guids, bool useCachedResult = true);
        Task<long> GetCountAsync(bool useCachedResult = true);
        Task<bool> DeleteAllAsync();
        Task<bool> DeleteRangeAsync(IEnumerable<T> entities);
        Task<bool> DeleteByGuidAsync(string guid);
        Task<bool> DeleteAsync(T entity);
        Task<bool> DeleteRangeByGuidsAsync(IEnumerable<Guid> guids);
        Task<bool> DeleteRangeByGuidsAsync(IEnumerable<string> guids);
        Task<bool> IsAnyItemAsync();
        Task<int> SaveChangesAsync();
        Task DatabaseShrinkMemoryAsync(CancellationToken cancellationToken = default);
        void DabataseClearChangeTracker();
        void MarkForDelete<T1>(T1 entity);
        void MarkForUpdate<T1>(T1 entity);
        /// <summary>
        /// Updates all entities matching the provided <paramref name="predicate"/> in the database 
        /// using the specified set of property updates.<br />
        /// 
        /// <b>⚠️ WARNING: </b><br />
        /// This method uses 
        /// <see cref="EntityFrameworkQueryableExtensions.ExecuteUpdateAsync{T}"/>, 
        /// which directly updates records in the database. <br />
        /// However, it does not automatically update the corresponding tracked entities in memory. <br />
        /// Therefore:<br />
        /// - If you want the entities in memory to reflect the changes made in the database, <b>you must reload them</b>.<br /><br />
        /// 
        /// <para>
        /// If <paramref name="reloadTrackedEntities"/> is set to <see langword="true"/>, 
        /// the method will reload the affected entities from the database,<br />
        /// ensuring the tracked entities are updated to reflect the latest database state.
        /// </para>
        /// <para>
        /// If <paramref name="reloadTrackedEntities"/> is set to <see langword="false"/>, 
        /// the method will not reload entities from the database, 
        /// and tracked entities will remain in their current state, <br />
        /// potentially leading to inconsistencies between in-memory entities and database state.
        /// </para>
        /// </summary>
        /// <param name="predicate">A filter expression to identify the entities to update.</param>
        /// <param name="setPropertyCalls">A set of property updates to apply to the matching entities.</param>
        /// <param name="reloadTrackedEntities">A flag indicating whether to reload entities from the database after the update.</param>
        /// <returns>Returns <c>true</c> if the update operation was successful.</returns>
        Task<bool> ExecuteUpdateAsync(
            Expression<Func<T, bool>> predicate,
            Expression<Func<SetPropertyCalls<T>, SetPropertyCalls<T>>> setPropertyCalls,
            bool reloadTrackedEntities = false);
        /// <summary>
        /// Deletes all entities matching the provided <paramref name="predicate"/> from the database.<br />
        /// 
        /// <b>⚠️ WARNING: </b><br />
        /// This method uses 
        /// <see cref="EntityFrameworkQueryableExtensions.ExecuteDeleteAsync{TSource}(IQueryable{TSource}, CancellationToken){T}"/>, 
        /// which directly deletes records in the database. <br />
        /// However, it does not automatically remove the corresponding tracked entities in memory. <br />
        /// Therefore:<br />
        /// - If you want the entities in memory to reflect the deletion, <b>you must reload them</b>.<br /><br />
        /// 
        /// <para>
        /// If <paramref name="reloadTrackedEntities"/> is set to <see langword="true"/>, 
        /// the method will reload the affected entities from the database,<br />
        /// ensuring the tracked entities are updated to reflect the latest database state.
        /// </para>
        /// <para>
        /// If <paramref name="reloadTrackedEntities"/> is set to <see langword="false"/>, 
        /// the method will not reload entities from the database, 
        /// and tracked entities will remain in their current state, <br />
        /// potentially leading to inconsistencies between in-memory entities and database state.
        /// </para>
        /// </summary>
        /// <param name="predicate">A filter expression to identify the entities to update.</param>
        /// <param name="reloadTrackedEntities">A flag indicating whether to reload entities from the database after the update.</param>
        /// <returns>Returns <see langword="true"/> if the delete operation was successful.</returns>
        Task<bool> ExecuteDeleteAsync(Expression<Func<T, bool>> predicate, bool reloadTrackedEntities = false);
    }
}
