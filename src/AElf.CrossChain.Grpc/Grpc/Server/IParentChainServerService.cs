using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AElf.Contracts.CrossChain;
using AElf.Kernel;
using AElf.Kernel.Blockchain.Application;
using Google.Protobuf;
using Volo.Abp.DependencyInjection;

namespace AElf.CrossChain.Grpc
{
    public interface IParentChainServerService
    {
        Task<ResponseParentChainBlockData> GenerateResponseAsync(Block block, int remoteSideChainId);

        Task<ChainInitializationContext> GetChainInitializationContextAsync(int chainId, LastIrreversibleBlockDto libDto);
    }

    public class ParentChainServerService : IParentChainServerService, ITransientDependency
    {
        private readonly IBlockExtraDataService _blockExtraDataService;
        private readonly IBasicCrossChainDataProvider _crossChainDataProvider;

        public ParentChainServerService(IBlockExtraDataService blockExtraDataService, IBasicCrossChainDataProvider crossChainDataProvider)
        {
            _blockExtraDataService = blockExtraDataService;
            _crossChainDataProvider = crossChainDataProvider;
        }

        public async Task<ResponseParentChainBlockData> GenerateResponseAsync(Block block, int remoteSideChainId)
        {
            var responseParentChainBlockData = new ResponseParentChainBlockData
            {
                Success = true,
                BlockData = new ParentChainBlockDataInResponse
                {
                    Root = new ParentChainBlockRootInfo
                    {
                        ParentChainHeight = block.Height,
                        ParentChainId = block.Header.ChainId
                    }
                }
            };
            responseParentChainBlockData = FillExtraDataInResponse(responseParentChainBlockData, block.Header);

            if (responseParentChainBlockData.BlockData.Root.CrossChainExtraData == null)
                return responseParentChainBlockData;

            var indexedSideChainBlockDataResult = await GetIndexedSideChainBlockInfoResult(block);
            var enumerableMerklePath = GetEnumerableMerklePath(indexedSideChainBlockDataResult, remoteSideChainId);
            foreach (var (sideChainHeight, merklePath) in enumerableMerklePath)
            {
                responseParentChainBlockData.BlockData.IndexedMerklePath.Add(sideChainHeight, merklePath);
            }

            return responseParentChainBlockData;
        }

        public async Task<ChainInitializationContext> GetChainInitializationContextAsync(int chainId, LastIrreversibleBlockDto libDto)
        {
            var message = await _crossChainDataProvider.GetChainInitializationContextAsync(chainId, libDto.BlockHash, libDto.BlockHeight);
            return message==null ? null : ChainInitializationContext.Parser.ParseFrom(message.ToByteString());
        }
        
        private ResponseParentChainBlockData FillExtraDataInResponse(ResponseParentChainBlockData
            responseParentChainBlockData, BlockHeader blockHeader)
        {
            var transactionStatusMerkleRoot = GetTransactionStatusMerkleTreeRootFromHeader(blockHeader);

            responseParentChainBlockData.BlockData.Root.TransactionStatusMerkleRoot = transactionStatusMerkleRoot;

            var crossChainExtraByteString = GetExtraDataFromHeader(blockHeader, "CrossChain");
            var crossChainExtra = crossChainExtraByteString == ByteString.Empty || crossChainExtraByteString == null
                ? null
                : CrossChainExtraData.Parser.ParseFrom(crossChainExtraByteString);
            responseParentChainBlockData.BlockData.Root.CrossChainExtraData = crossChainExtra;

            responseParentChainBlockData.BlockData.ExtraData.Add(GetExtraDataForExchange(blockHeader,
                new[] {"Consensus"}));
            return responseParentChainBlockData;
        }
        
        private async Task<List<SideChainBlockData>> GetIndexedSideChainBlockInfoResult(Block block)
        {
            var message =
                await _crossChainDataProvider.GetIndexedCrossChainBlockDataAsync(block.GetHash(), block.Height);
            //Logger.LogTrace($"Indexed side chain block size {crossChainBlockData.SideChainBlockData.Count}");
            var crossChainBlockData = CrossChainBlockData.Parser.ParseFrom(message.ToByteString());
            return crossChainBlockData.SideChainBlockData
                .Select(m => SideChainBlockData.Parser.ParseFrom(m.ToByteString())).ToList();
        }
        
        private IEnumerable<(long, MerklePath)> GetEnumerableMerklePath(IList<SideChainBlockData> indexedSideChainBlockDataResult, 
            int sideChainId)
        {
            var binaryMerkleTree = new BinaryMerkleTree();
            foreach (var blockInfo in indexedSideChainBlockDataResult)
            {
                binaryMerkleTree.AddNode(blockInfo.TransactionMerkleTreeRoot);
            }

            binaryMerkleTree.ComputeRootHash();
            // This is to tell side chain the merkle path for one side chain block,
            // which could be removed with subsequent improvement.
            var merklepathList = new List<(long, MerklePath)>();
            for (var i = 0; i < indexedSideChainBlockDataResult.Count; i++)
            {
                var info = indexedSideChainBlockDataResult[i];
                if (!info.ChainId.Equals(sideChainId))
                    continue;
                var merklePath = binaryMerkleTree.GenerateMerklePath(i);
                merklepathList.Add((info.Height, merklePath));
            }
            return merklepathList;
        }
        
        private Hash GetTransactionStatusMerkleTreeRootFromHeader(BlockHeader header)
        {
            return Hash.Parser.ParseFrom(_blockExtraDataService.GetMerkleTreeRootExtraDataForTransactionStatus(header));
        }
        
        private ByteString GetExtraDataFromHeader(BlockHeader header, string symbol)
        {
            return _blockExtraDataService.GetExtraDataFromBlockHeader(symbol, header);
        }
        
        private Dictionary<string, ByteString> GetExtraDataForExchange(BlockHeader header, IEnumerable<string> symbolsOfExchangedExtraData)
        {
            var res = new Dictionary<string, ByteString>();
            foreach (var symbol in symbolsOfExchangedExtraData)
            {
                var extraData = GetExtraDataFromHeader(header, symbol);
                if(extraData != null)
                    res.Add(symbol, extraData);
            }
            return res;
        }
    }
}
