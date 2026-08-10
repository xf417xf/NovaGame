using Microsoft.EntityFrameworkCore.Storage;
using SwordMaster.Application.PlayerAssets;

namespace SwordMaster.Host.EntityFrameworkCore.PlayerAssets;

public sealed class PlayerAssetsTransactionManager : IPlayerAssetsTransactionManager
{
    private readonly SwordMasterDbContext _dbContext;

    public PlayerAssetsTransactionManager(SwordMasterDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<IPlayerAssetsTransactionScope> BeginAsync(
        CancellationToken cancellationToken = default)
    {
        if (_dbContext.Database.CurrentTransaction is not null)
        {
            throw new InvalidOperationException(
                "A player-assets transaction is already active for this request scope.");
        }

        var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        return new PlayerAssetsTransactionScope(_dbContext, transaction);
    }

    private sealed class PlayerAssetsTransactionScope : IPlayerAssetsTransactionScope
    {
        private readonly SwordMasterDbContext _dbContext;
        private readonly IDbContextTransaction _transaction;
        private bool _completed;
        private bool _rolledBack;
        private bool _disposed;

        public PlayerAssetsTransactionScope(
            SwordMasterDbContext dbContext,
            IDbContextTransaction transaction)
        {
            _dbContext = dbContext;
            _transaction = transaction;
        }

        public async Task CompleteAsync(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_completed)
            {
                throw new InvalidOperationException(
                    "The player-assets transaction has already completed.");
            }

            try
            {
                await _dbContext.SaveChangesAsync(cancellationToken);
                await _transaction.CommitAsync(cancellationToken);
                _completed = true;
            }
            catch
            {
                await _transaction.RollbackAsync(CancellationToken.None);
                _rolledBack = true;
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed)
            {
                return;
            }

            if (!_completed && !_rolledBack)
            {
                await _transaction.RollbackAsync(CancellationToken.None);
                _rolledBack = true;
            }

            await _transaction.DisposeAsync();
            _disposed = true;
        }
    }
}
