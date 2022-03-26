using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethermind.Core.Crypto;

namespace Nethermind.State.Snap
{
    public class ProgressTracker
    {
        private const int STORAGE_BATCH_SIZE = 1000;

        private AccountRange AccountRangeRequested;

        public Keccak NextAccountPath { get; set; } = Keccak.Zero; //new("0xfe00000000000000000000000000000000000000000000000000000000000000");
        private ConcurrentQueue<StorageRange> NextSlotRange { get; set; } = new();
        private ConcurrentQueue<PathWithAccount> StoragesToRetrieve { get; set; } = new();

        public bool MoreAccountsToRight { get; set; } = true;

        public (AccountRange accountRange, StorageRange storageRange) GetNextRequest(long blockNumber, Keccak rootHash)
        {
            if (NextSlotRange.TryDequeue(out StorageRange storageRange))
            {
                storageRange.RootHash = rootHash;
                storageRange.BlockNumber = blockNumber;

                return (null, storageRange);
            }
            else if (StoragesToRetrieve.Count > 0)
            {
                // TODO: optimize this
                List<PathWithAccount> storagesToQuery = storagesToQuery = new(STORAGE_BATCH_SIZE);

                for (int i = 0; i < STORAGE_BATCH_SIZE && StoragesToRetrieve.TryDequeue(out PathWithAccount storage); i++)
                {
                    storagesToQuery.Add(storage);
                }

                StorageRange storageRange2 = new()
                {
                    RootHash = rootHash,
                    Accounts = storagesToQuery.ToArray(),
                    StartingHash = Keccak.Zero,
                    BlockNumber = blockNumber
                };

                return (null, storageRange2);
            }
            else if (MoreAccountsToRight && AccountRangeRequested is null)
            {
                // some contract hardcoded
                //var path = Keccak.Compute(new Address("0x4c9A3f79801A189D98D3a5A18dD5594220e4d907").Bytes);
                // = new(_bestHeader.StateRoot, path, path, _bestHeader.Number);

                AccountRangeRequested = new(rootHash, NextAccountPath, Keccak.MaxValue, blockNumber);

                return (AccountRangeRequested, null);
            }

            return (null, null);
        }

        public void EnqueueAccountStorage(PathWithAccount storage)
        {
            StoragesToRetrieve.Enqueue(storage);
        }

        public void EnqueueAccountStorage(StorageRange storageRange)
        {
            NextSlotRange.Enqueue(storageRange);
        }

        public void ReportRequestFinished(AccountRange accountRange)
        {
            AccountRangeRequested = null;
        }

        public bool IsSnapGetRangesFinished()
        {
            return !MoreAccountsToRight && StoragesToRetrieve.Count == 0 && AccountRangeRequested == null && NextSlotRange.Count == 0;
        }
    }
}
