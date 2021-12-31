﻿//  Copyright (c) 2021 Demerzel Solutions Limited
//  This file is part of the Nethermind library.
// 
//  The Nethermind library is free software: you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as published by
//  the Free Software Foundation, either version 3 of the License, or
//  (at your option) any later version.
// 
//  The Nethermind library is distributed in the hope that it will be useful,
//  but WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
//  GNU Lesser General Public License for more details.
// 
//  You should have received a copy of the GNU Lesser General Public License
//  along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Threading.Tasks;
using Nethermind.Api;
using Nethermind.Api.Extensions;
using Nethermind.Blockchain;
using Nethermind.Blockchain.Synchronization;
using Nethermind.Consensus;
using Nethermind.Consensus.Rewards;
using Nethermind.Consensus.Validators;
using Nethermind.Core;
using Nethermind.Db;
using Nethermind.JsonRpc.Modules;
using Nethermind.Logging;
using Nethermind.Merge.Plugin.Handlers;
using Nethermind.Merge.Plugin.Handlers.V1;
using Nethermind.Merge.Plugin.Synchronization;

namespace Nethermind.Merge.Plugin
{
    public partial class MergePlugin : IConsensusWrapperPlugin, ISynchronizationPlugin
    {
        private INethermindApi _api = null!;
        private ILogger _logger = null!;
        private IMergeConfig _mergeConfig = null!;
        private IPoSSwitcher _poSSwitcher = NoPoS.Instance;
        private IBeaconPivot? _beaconPivot;
        private ManualBlockFinalizationManager _blockFinalizationManager = null!;

        public string Name => "Merge";
        public string Description => "Merge plugin for ETH1-ETH2";
        public string Author => "Nethermind";

        public Task Init(INethermindApi nethermindApi)
        {
            _api = nethermindApi;
            _mergeConfig = nethermindApi.Config<IMergeConfig>();
            _logger = _api.LogManager.GetClassLogger();

            if (_mergeConfig.Enabled)
            {
                if (_api.DbProvider == null) throw new ArgumentException(nameof(_api.DbProvider));
                if (_api.BlockTree == null) throw new ArgumentException(nameof(_api.BlockTree));
                if (_api.SpecProvider == null) throw new ArgumentException(nameof(_api.SpecProvider));

                _beaconPivot = new BeaconPivot(_api.BlockTree, _api.LogManager);
                _poSSwitcher = new PoSSwitcher(_mergeConfig,
                    _api.DbProvider.GetDb<IDb>(DbNames.Metadata), _api.BlockTree, _api.SpecProvider, _api.LogManager);
                _blockFinalizationManager = new ManualBlockFinalizationManager();

                Address address;
                if (string.IsNullOrWhiteSpace(_mergeConfig.BlockAuthorAccount))
                {
                    address = Address.Zero;
                }
                else
                {
                    address = new Address(_mergeConfig.BlockAuthorAccount);
                }

                ISigner signer = new Eth2Signer(address);

                _api.RewardCalculatorSource = new MergeRewardCalculatorSource(
                   _api.RewardCalculatorSource ?? NoBlockRewards.Instance,  _poSSwitcher);
                _api.SealEngine = new MergeSealEngine(_api.SealEngine, _poSSwitcher, signer, _api.LogManager);
                _api.SealValidator = _api.SealEngine;
                _api.Sealer = _api.SealEngine;
                _api.GossipPolicy = new MergeGossipPolicy(_api.GossipPolicy, _poSSwitcher, _blockFinalizationManager);
            }

            return Task.CompletedTask;
        }

        public Task InitNetworkProtocol()
        {
            if (_mergeConfig.Enabled)
            {
                _api.HealthHintService =
                    new MergeHealthHintService(_api.HealthHintService, _poSSwitcher);

                _api.FinalizationManager = _blockFinalizationManager;
            }

            return Task.CompletedTask;
        }

        public Task InitRpcModules()
        {
            if (_mergeConfig.Enabled)
            {
                if (_api.RpcModuleProvider is null) throw new ArgumentNullException(nameof(_api.RpcModuleProvider));
                if (_api.BlockTree is null) throw new ArgumentNullException(nameof(_api.BlockTree));
                if (_api.BlockchainProcessor is null) throw new ArgumentNullException(nameof(_api.BlockchainProcessor));
                if (_api.StateProvider is null) throw new ArgumentNullException(nameof(_api.StateProvider));
                if (_api.HeaderValidator is null) throw new ArgumentNullException(nameof(_api.HeaderValidator));
                if (_api.EthSyncingInfo is null) throw new ArgumentNullException(nameof(_api.EthSyncingInfo));
                if (_api.Sealer is null) throw new ArgumentNullException(nameof(_api.Sealer));
                if (_api.BlockValidator is null) throw new ArgumentNullException(nameof(_api.BlockValidator));
                if (_api.BlockProcessingQueue is null) throw new ArgumentNullException(nameof(_api.BlockProcessingQueue));
                if (_beaconPivot is null) throw new ArgumentNullException(nameof(_beaconPivot));
                
                IInitConfig? initConfig = _api.Config<IInitConfig>();
                PayloadStorage payloadStorage = new(_idealBlockProductionContext, _emptyBlockProductionContext, initConfig, _api.LogManager);
                PayloadService payloadService = new (_idealBlockProductionContext,
                    _emptyBlockProductionContext, initConfig, _api.Sealer, _api.LogManager);
                
                IEngineRpcModule engineRpcModule = new EngineRpcModule(
                    new PreparePayloadHandler(_api.BlockTree, payloadStorage, _manualTimestamper, _api.Sealer,
                        _api.LogManager),
                    new GetPayloadHandler(payloadStorage, _api.LogManager),
                    new GetPayloadV1Handler(payloadService, _api.LogManager),
                    new ExecutePayloadHandler(
                        _api.HeaderValidator,
                        _api.BlockTree,
                        _api.BlockchainProcessor,
                        _api.EthSyncingInfo,
                        _api.Config<IInitConfig>(),
                        _api.LogManager),
                    new ExecutePayloadV1Handler(
                        _api.BlockValidator,
                        _api.BlockTree,
                        _api.BlockchainProcessor,
                        _api.EthSyncingInfo,
                        _api.Config<IInitConfig>(),
                        _mergeConfig,
                        _api.Synchronizer!,
                        _beaconPivot,
                        _api.LogManager),
                    new ForkChoiceUpdatedHandler(_api.BlockTree, _api.StateProvider, _blockFinalizationManager,
                        _poSSwitcher, _api.BlockConfirmationManager, _api.LogManager),
                    new ForkchoiceUpdatedV1Handler(_api.BlockTree, _api.StateProvider, _blockFinalizationManager,
                        _poSSwitcher, _api.EthSyncingInfo, _api.BlockConfirmationManager, payloadService, _mergeConfig, _api.BlockchainProcessor, _api.Synchronizer, _api.DbProvider!.StateDb, _api.LogManager),
                    new ExecutionStatusHandler(_api.BlockTree, _api.BlockConfirmationManager,
                        _blockFinalizationManager),
                    _api.LogManager,
                    _api.BlockTree);

                _api.RpcModuleProvider.RegisterSingle(engineRpcModule);
                if (_logger.IsInfo) _logger.Info("Engine Module has been enabled");
            }
            
            return Task.CompletedTask;
        }

        public Task InitSynchronization()
        {
            if (_api.SyncModeSelector is null) throw new ArgumentNullException(nameof(_api.SyncModeSelector));
            if (_api.SpecProvider is null) throw new ArgumentNullException(nameof(_api.SpecProvider));
            if (_api.SyncPeerPool is null) throw new ArgumentNullException(nameof(_api.SyncPeerPool));
            if (_api.BlockTree is null) throw new ArgumentNullException(nameof(_api.BlockTree));
            if (_beaconPivot is null) throw new ArgumentNullException(nameof(_beaconPivot));
            
            // ToDo strange place for validators initialization
            _api.HeaderValidator = new PostMergeHeaderValidator(_poSSwitcher, _api.BlockTree, _api.SpecProvider, Always.Valid, _api.LogManager);
            _api.BlockValidator = new BlockValidator(_api.TxValidator, _api.HeaderValidator, Always.Valid,
                _api.SpecProvider, _api.LogManager);
            
            _api.SyncModeSelector = new MergeSyncModeSelector(_api.SyncModeSelector);
            _api.BlockDownloaderFactory = new MergeBlockDownloaderFactory(_beaconPivot, _api.SpecProvider, _api.BlockTree,
                _api.ReceiptStorage!,
                _api.BlockValidator!,
                _api.SealValidator!,
                _api.SyncPeerPool,
                _api.NodeStatsManager!,
                _api.SyncModeSelector!,
                _api.Config<ISyncConfig>(),
                _api.LogManager);
            
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        public string SealEngineType => "Eth2Merge";
    }
}
