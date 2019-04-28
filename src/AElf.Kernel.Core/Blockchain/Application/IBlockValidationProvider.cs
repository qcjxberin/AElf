using System;
using System.Runtime.Serialization;
using System.Threading.Tasks;
using AElf.Kernel.Account.Application;

namespace AElf.Kernel.Blockchain.Application
{
    public interface IBlockValidationProvider
    {
        Task<bool> ValidateBeforeAttachAsync(IBlock block);
        Task<bool> ValidateBlockBeforeExecuteAsync(IBlock block);
        Task<bool> ValidateBlockAfterExecuteAsync(IBlock block);
    }


    [Serializable]
    public class BlockValidationException : Exception
    {
        //
        // For guidelines regarding the creation of new exception types, see
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
        // and
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
        //

        public BlockValidationException()
        {
        }

        public BlockValidationException(string message) : base(message)
        {
        }

        public BlockValidationException(string message, Exception inner) : base(message, inner)
        {
        }

        protected BlockValidationException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }
    }

    [Serializable]
    public class ValidateNextTimeBlockValidationException : BlockValidationException
    {
        //
        // For guidelines regarding the creation of new exception types, see
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/cpgenref/html/cpconerrorraisinghandlingguidelines.asp
        // and
        //    http://msdn.microsoft.com/library/default.asp?url=/library/en-us/dncscol/html/csharp07192001.asp
        //

        public ValidateNextTimeBlockValidationException()
        {
        }

        public ValidateNextTimeBlockValidationException(string message) : base(message)
        {
        }

        public ValidateNextTimeBlockValidationException(string message, Exception inner) : base(message, inner)
        {
        }

        public ValidateNextTimeBlockValidationException(Hash blockhash) : this(
            $"validate next time, block hash = {blockhash.ToHex()}")
        {
            BlockHash = blockhash;
        }

        protected ValidateNextTimeBlockValidationException(
            SerializationInfo info,
            StreamingContext context) : base(info, context)
        {
        }

        public Hash BlockHash { get; private set; }
    }

    public class BlockValidationProvider : IBlockValidationProvider
    {
        private IBlockchainService _blockchainServce;
        public BlockValidationProvider(IBlockchainService blockchainService)
        {
            _blockchainServce = blockchainService;
        }

        public async Task<bool> ValidateBeforeAttachAsync(IBlock block)
        {
            if (block?.Header == null || block.Body == null)
                return false;

            if (block.Body.TransactionsCount == 0)
                return false;

            if (_blockchainServce.GetChainId() != block.Header.ChainId)
                return false;

            var chain = await _blockchainServce.GetChainAsync();
            var blockHeader = block.Header;
            while (true)
            {
                if (blockHeader.Height <= chain.LastIrreversibleBlockHeight)
                {
                    return false;
                }

                if (blockHeader.PreviousBlockHash == chain.LastIrreversibleBlockHash)
                {
                    break;
                }

                blockHeader = await _blockchainServce.GetBlockHeaderByHashAsync(blockHeader.PreviousBlockHash);

                if (blockHeader == null)
                {
                    break;
                }
            }

            if (block.Height != KernelConstants.GenesisBlockHeight && !block.VerifySignature())
                return false;

            if (block.Body.CalculateMerkleTreeRoots() != block.Header.MerkleTreeRootOfTransactions)
                return false;

            if (block.Height != KernelConstants.GenesisBlockHeight &&
                block.Header.Time.ToDateTime() - DateTime.UtcNow > KernelConsts.AllowedFutureBlockTimeSpan)
                return false;

            return true;
        }

        public async Task<bool> ValidateBlockBeforeExecuteAsync(IBlock block)
        {
            if (block?.Header == null || block.Body == null)
                return false;

            if (block.Body.TransactionsCount == 0)
                return false;

            return true;
        }

        public async Task<bool> ValidateBlockAfterExecuteAsync(IBlock block)
        {
            return true;
        }
    }
}