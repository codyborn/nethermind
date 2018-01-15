﻿/*
 * Copyright (c) 2018 Demerzel Solutions Limited
 * This file is part of the Nethermind library.
 *
 * The Nethermind library is free software: you can redistribute it and/or modify
 * it under the terms of the GNU Lesser General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * The Nethermind library is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
 * GNU Lesser General Public License for more details.
 *
 * You should have received a copy of the GNU Lesser General Public License
 * along with the Nethermind. If not, see <http://www.gnu.org/licenses/>.
 */

using NUnit.Framework;

namespace Nevermind.Core.Test
{
    [TestFixture]
    public class TransactionTests
    {
        [Test]
        public void When_init_and_data_empty_then_is_transfer()
        {
            Transaction transaction = new Transaction();
            transaction.Init = null;
            transaction.Data = null;
            Assert.True(transaction.IsTransfer, nameof(Transaction.IsTransfer));
            Assert.False(transaction.IsMessageCall, nameof(Transaction.IsMessageCall));
            Assert.False(transaction.IsContractCreation, nameof(Transaction.IsContractCreation));
        }

        [Test]
        public void When_init_empty_and_data_not_empty_then_is_message_call()
        {
            Transaction transaction = new Transaction();
            transaction.Init = null;
            transaction.Data = new byte[0];
            Assert.False(transaction.IsTransfer, nameof(Transaction.IsTransfer));
            Assert.True(transaction.IsMessageCall, nameof(Transaction.IsMessageCall));
            Assert.False(transaction.IsContractCreation, nameof(Transaction.IsContractCreation));
        }

        [Test]
        public void When_init_not_empty_and_data_empty_then_is_message_call()
        {
            Transaction transaction = new Transaction();
            transaction.Init = new byte[0];
            transaction.Data = null;
            Assert.False(transaction.IsTransfer, nameof(Transaction.IsTransfer));
            Assert.False(transaction.IsMessageCall, nameof(Transaction.IsMessageCall));
            Assert.True(transaction.IsContractCreation, nameof(Transaction.IsContractCreation));
        }
    }
}