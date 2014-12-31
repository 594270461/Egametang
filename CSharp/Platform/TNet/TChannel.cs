﻿using System;
using System.Threading.Tasks;
using Common.Helper;
using Common.Logger;
using MongoDB.Bson;
using Network;

namespace TNet
{
	internal class TChannel: IChannel
	{
		private const int SendInterval = 50;

		private readonly TService service;
		private TSocket socket;

		private readonly TBuffer recvBuffer = new TBuffer();
		private readonly TBuffer sendBuffer = new TBuffer();

		private ObjectId sendTimer = ObjectId.Empty;
		private Action onParseComplete = () => { };
		private readonly PacketParser parser;

		public TChannel(TSocket socket, TService service)
		{
			this.socket = socket;
			this.service = service;
			this.parser = new PacketParser(recvBuffer);
		}

		public void Dispose()
		{
			if (socket == null)
			{
				return;
			}
			this.service.Remove(this);
			socket.Dispose();
			this.socket = null;
		}

		public void SendAsync(byte[] buffer, byte channelID = 0, PacketFlags flags = PacketFlags.Reliable)
		{
			byte[] size = BitConverter.GetBytes(buffer.Length);
			this.sendBuffer.SendTo(size);
			this.sendBuffer.SendTo(buffer);
			if (this.sendTimer == ObjectId.Empty)
			{
				this.sendTimer = this.service.Timer.Add(TimeHelper.Now() + SendInterval, this.SendTimerCallback);
			}
		}

		public ObjectId SendTimer
		{
			get
			{
				return this.sendTimer;
			}
		}

		private async void SendTimerCallback()
		{
			try
			{
				while (true)
				{
					if (this.sendBuffer.Count == 0)
					{
						break;
					}
					int sendSize = TBuffer.ChunkSize - this.sendBuffer.FirstIndex;
					if (sendSize > this.sendBuffer.Count)
					{
						sendSize = this.sendBuffer.Count;
					}
					int n = await this.socket.SendAsync(
						this.sendBuffer.First, this.sendBuffer.FirstIndex, sendSize);
					this.sendBuffer.FirstIndex += n;
					if (this.sendBuffer.FirstIndex == TBuffer.ChunkSize)
					{
						this.sendBuffer.FirstIndex = 0;
						this.sendBuffer.RemoveFirst();
					}
				}
			}
			catch (Exception e)
			{
				Log.Trace(e.ToString());
			}

			this.sendTimer = ObjectId.Empty;
		}

		public Task<byte[]> RecvAsync()
		{
			var tcs = new TaskCompletionSource<byte[]>();

			if (parser.Parse())
			{
				tcs.SetResult(parser.GetPacket());
			}
			else
			{
				this.onParseComplete = () => this.ParseComplete(tcs);	
			}
			return tcs.Task;
		}

		public async Task<bool> DisconnnectAsync()
		{
			return await this.socket.DisconnectAsync();
		}

		public string RemoteAddress
		{
			get
			{
				return this.socket.RemoteAddress;
			}
		}

		private void ParseComplete(TaskCompletionSource<byte[]> tcs)
		{
			byte[] packet = parser.GetPacket();
			this.onParseComplete = () => { };
			tcs.SetResult(packet);
		}

		public async void Start()
		{
			try
			{
				while (true)
				{
					int n = await this.socket.RecvAsync(
						this.recvBuffer.Last, this.recvBuffer.LastIndex, TBuffer.ChunkSize - this.recvBuffer.LastIndex);
					if (n == 0)
					{
						break;
					}

					this.recvBuffer.LastIndex += n;
					if (this.recvBuffer.LastIndex == TBuffer.ChunkSize)
					{
						this.recvBuffer.AddLast();
						this.recvBuffer.LastIndex = 0;
					}

					// 解析封包
					if (parser.Parse())
					{
						this.onParseComplete();
					}
				}
			}
			catch (Exception e)
			{
				Log.Trace(e.ToString());
			}
		}
	}
}