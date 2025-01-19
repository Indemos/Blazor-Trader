using Distribution.Services;
using Distribution.Stream;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Terminal.Core.Domains;
using Terminal.Core.Enums;
using Terminal.Core.Extensions;
using Terminal.Core.Models;
using Terminal.Core.Services;
using Tradier.Client;
using Tradier.Endpoints;
using Tradier.Mappers;
using Tradier.Messages;
using Dis = Distribution.Stream.Models;

namespace Tradier
{
  public class Adapter : Gateway
  {
    /// <summary>
    /// HTTP client
    /// </summary>
    protected Service _sender;

    /// <summary>
    /// Event session
    /// </summary>
    protected string _dataSession;

    /// <summary>
    /// Account session
    /// </summary>
    protected string _accountSession;

    /// <summary>
    /// Web socket for events
    /// </summary>
    protected ClientWebSocket _dataStreamer;

    /// <summary>
    /// Web socket for account
    /// </summary>
    protected ClientWebSocket _accountStreamer;

    /// <summary>
    /// Disposable connections
    /// </summary>
    protected IList<IDisposable> _subscriptions;

    /// <summary>
    /// API key
    /// </summary>
    public string Token { get; set; }

    /// <summary>
    /// API key for streaming
    /// </summary>
    public string SessionToken { get; set; }

    /// <summary>
    /// HTTP endpoint
    /// </summary>
    public string DataUri { get; set; }

    /// <summary>
    /// Socket endpoint
    /// </summary>
    public string StreamUri { get; set; }

    /// <summary>
    /// Streaming authentication endpoint
    /// </summary>
    public string SessionUri { get; set; }

    /// <summary>
    /// Client
    /// </summary>
    public TradierClient Client { get; protected set; }

    /// <summary>
    /// Constructor
    /// </summary>
    public Adapter()
    {
      _subscriptions = [];

      DataUri = "https://sandbox.tradier.com/v1";
      SessionUri = "https://api.tradier.com/v1";
      StreamUri = "wss://ws.tradier.com/v1";
    }

    public override async Task<ResponseModel<StatusEnum>> Connect()
    {
      var response = new ResponseModel<StatusEnum>();

      try
      {
        var sender = new Service();
        var scheduler = new ScheduleService();
        var dataStreamer = new ClientWebSocket();
        var accountStreamer = new ClientWebSocket();
        var streamingEndpoints = new StreamingEndpoints { Adapter = this };

        await Disconnect();

        Client = new TradierClient(Token, Account.Descriptor);

        _sender = sender;
        _dataStreamer = dataStreamer;
        _accountStreamer = accountStreamer;
        _dataSession = $"{(await streamingEndpoints.GetMarketSession())?.Stream?.Session}";
        _accountSession = $"{(await streamingEndpoints.GetAccountSession())?.Stream?.Session}";

        await GetAccount([]);
        await GetConnection("/v1/markets/events", dataStreamer, scheduler, message =>
        {
          switch ($"{message["type"]}")
          {
            case "quote":

              var quoteMessage = message.Deserialize<QuoteMessage>();
              var point = InternalMap.GetPoint(quoteMessage);
              var instrument = Account.Instruments[quoteMessage.Symbol];

              point.Instrument = instrument;

              instrument.Points.Add(point);
              instrument.PointGroups.Add(point, instrument.TimeFrame);

              break;

            case "trade": break;
            case "tradex": break;
            case "summary": break;
            case "timesale": break;
          }
        });

        await GetConnection("/v1/accounts/events", accountStreamer, scheduler, message =>
        {
          var order = InternalMap.GetStreamOrder(message.Deserialize<OrderMessage>());
          var container = new MessageModel<OrderModel> { Next = order };

          OrderStream(container);
        });

        _subscriptions.Add(sender);
        _subscriptions.Add(scheduler);
        _subscriptions.Add(dataStreamer);
        _subscriptions.Add(accountStreamer);

        await Task.WhenAll(Account.Instruments.Values.Select(Subscribe));

        response.Data = StatusEnum.Success;
      }
      catch (Exception e)
      {
        response.Errors.Add(new ErrorModel { ErrorMessage = $"{e}" });
      }

      return response;
    }

    /// <summary>
    /// Save state and dispose
    /// </summary>
    public override Task<ResponseModel<StatusEnum>> Disconnect()
    {
      var response = new ResponseModel<StatusEnum>();

      try
      {
        _subscriptions?.ForEach(o => o?.Dispose());
        _subscriptions?.Clear();

        response.Data = StatusEnum.Success;
      }
      catch (Exception e)
      {
        response.Errors.Add(new ErrorModel { ErrorMessage = $"{e}" });
      }

      return Task.FromResult(response);
    }

    /// <summary>
    /// Subscribe to data streams
    /// </summary>
    /// <param name="instrument"></param>
    /// <returns></returns>
    public override async Task<ResponseModel<StatusEnum>> Subscribe(InstrumentModel instrument)
    {
      var response = new ResponseModel<StatusEnum>
      {
        Data = StatusEnum.Success
      };

      try
      {
        await Unsubscribe(instrument);

        var dataMessage = new DataMessage
        {
          Symbols = [instrument.Name],
          Filter = ["trade", "quote", "summary", "timesale", "tradex"],
          LineBreak = true,
          ValidOnly = false,
          AdvancedDetails = true,
          Session = _dataSession
        };

        var accountMessage = new AccountMessage
        {
          Events = ["order"],
          Session = _accountSession
        };

        await SendStream(_dataStreamer, dataMessage);
        await SendStream(_accountStreamer, accountMessage);

      }
      catch (Exception e)
      {
        response.Errors.Add(new ErrorModel { ErrorMessage = $"{e}" });
      }

      return response;
    }

    /// <summary>
    /// Unsubscribe from data streams
    /// </summary>
    /// <param name="instrument"></param>
    /// <returns></returns>
    public override Task<ResponseModel<StatusEnum>> Unsubscribe(InstrumentModel instrument)
    {
      var response = new ResponseModel<StatusEnum>
      {
        Data = StatusEnum.Success
      };

      return Task.FromResult(response);
    }

    /// <summary>
    /// Sync open balance, order, and positions 
    /// </summary>
    /// <param name="criteria"></param>
    /// <returns></returns>
    public override async Task<ResponseModel<IAccount>> GetAccount(Hashtable criteria)
    {
      var response = new ResponseModel<IAccount>();

      try
      {
        var account = await Client.Account.GetBalances();
        var orders = (await Client.Account.GetOrders(Account.Descriptor))?.Order?.Select(InternalMap.GetOrder) ?? [];
        var positions = (await Client.Account.GetPositions(Account.Descriptor))?.Position?.Select(InternalMap.GetPosition) ?? [];

        Account.Balance = account.TotalEquity;
        Account.Orders = orders.GroupBy(o => o.Id).ToDictionary(o => o.Key, o => o.FirstOrDefault()).Concurrent();
        Account.Positions = positions.GroupBy(o => o.Name).ToDictionary(o => o.Key, o => o.FirstOrDefault()).Concurrent();

        positions
          .Where(o => Account.Instruments.ContainsKey(o.Name) is false)
          .ForEach(o => Account.Instruments[o.Name] = o.Transaction.Instrument);

        response.Data = Account;
      }
      catch (Exception e)
      {
        response.Errors = [new ErrorModel { ErrorMessage = $"{e}" }];
      }

      return response;
    }

    /// <summary>
    /// Get latest quote
    /// </summary>
    /// <param name="screener"></param>
    /// <param name="criteria"></param>
    /// <returns></returns>
    public override Task<ResponseModel<DomModel>> GetDom(PointScreenerModel args, Hashtable criteria)
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// Get historical ticks
    /// </summary>
    /// <param name="screener"></param>
    /// <param name="criteria"></param>
    /// <returns></returns>
    public override Task<ResponseModel<IList<PointModel>>> GetPoints(PointScreenerModel args, Hashtable criteria)
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// Get options
    /// </summary>
    /// <param name="screener"></param>
    /// <param name="criteria"></param>
    /// <returns></returns>
    public override Task<ResponseModel<IList<InstrumentModel>>> GetOptions(InstrumentScreenerModel args, Hashtable criteria)
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// Get positions 
    /// </summary>
    /// <param name="screener"></param>
    /// <param name="criteria"></param>
    /// <returns></returns>
    public override Task<ResponseModel<IList<OrderModel>>> GetPositions(PositionScreenerModel args, Hashtable criteria)
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// Get orders
    /// </summary>
    /// <param name="screener"></param>
    /// <param name="criteria"></param>
    /// <returns></returns>
    public override Task<ResponseModel<IList<OrderModel>>> GetOrders(OrderScreenerModel args, Hashtable criteria)
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// Create orders
    /// </summary>
    /// <param name="orders"></param>
    /// <returns></returns>
    public override Task<ResponseModel<IList<OrderModel>>> CreateOrders(params OrderModel[] orders)
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// Cancel orders
    /// </summary>
    /// <param name="orders"></param>
    /// <returns></returns>
    public override Task<ResponseModel<IList<OrderModel>>> DeleteOrders(params OrderModel[] orders)
    {
      throw new NotImplementedException();
    }

    /// <summary>
    /// Send data to the API
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="source"></param>
    /// <param name="verb"></param>
    /// <param name="content"></param>
    /// <param name="token"></param>
    /// <returns></returns>
    public virtual async Task<Dis.ResponseModel<T>> Send<T>(string source, HttpMethod verb = null, object content = null, string token = null)
    {
      var uri = new UriBuilder(source);
      var message = new HttpRequestMessage { Method = verb ?? HttpMethod.Get };

      switch (true)
      {
        case true when Equals(message.Method, HttpMethod.Put):
        case true when Equals(message.Method, HttpMethod.Post):
        case true when Equals(message.Method, HttpMethod.Patch):
          message.Content = new StringContent(JsonSerializer.Serialize(content, _sender.Options), Encoding.UTF8, "application/json");
          break;
      }

      message.RequestUri = uri.Uri;
      message.Headers.Add("Accept", "application/json");
      message.Headers.Add("Authorization", $"Bearer {token ?? Token}");

      var response = await _sender.Send<T>(message, _sender.Options);

      if (response.Message.IsSuccessStatusCode is false)
      {
        throw new HttpRequestException(await response.Message.Content.ReadAsStringAsync(), null, response.Message.StatusCode);
      }

      return response;
    }

    /// <summary>
    /// Send data to web socket stream
    /// </summary>
    /// <param name="streamer"></param>
    /// <param name="data"></param>
    /// <param name="cancellation"></param>
    /// <returns></returns>
    protected virtual Task SendStream(ClientWebSocket streamer, object data, CancellationTokenSource cancellation = null)
    {
      var content = JsonSerializer.Serialize(data, _sender.Options);
      var message = Encoding.ASCII.GetBytes(content);

      return streamer.SendAsync(
        message,
        WebSocketMessageType.Text,
        true,
        cancellation?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// Web socket stream
    /// </summary>
    /// <param name="uri"></param>
    /// <param name="streamer"></param>
    /// <param name="scheduler"></param>
    /// <param name="action"></param>
    /// <returns></returns>
    protected virtual async Task GetConnection(string uri, ClientWebSocket streamer, ScheduleService scheduler, Action<JsonNode> action)
    {
      var source = new UriBuilder($"{StreamUri}{uri}");
      var cancellation = new CancellationTokenSource();

      await streamer.ConnectAsync(source.Uri, cancellation.Token);

      scheduler.Send(async () =>
      {
        while (streamer.State is WebSocketState.Open)
        {
          try
          {
            var data = new byte[short.MaxValue];
            var streamResponse = await streamer.ReceiveAsync(data, cancellation.Token);
            var content = $"{Encoding.Default.GetString(data).Trim(['\0', '[', ']'])}";

            action(JsonNode.Parse(content));

          }
          catch (Exception e)
          {
            InstanceService<MessageService>.Instance.OnMessage(new MessageModel<string> { Error = e });
          }
        }
      });
    }
  }
}