using Alpaca;
using Alpaca.Markets;
using Canvas.Core.Shapes;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading.Tasks;
using Terminal.Components;
using Terminal.Core.Domains;
using Terminal.Core.Enums;
using Terminal.Core.Indicators;
using Terminal.Core.Models;
using Terminal.Services;

namespace Terminal.Pages.Gateways
{
  public partial class AlpacaDemo
  {
    [Inject] IConfiguration Configuration { get; set; }

    protected PageComponent View { get; set; }
    protected PerformanceIndicator Performance { get; set; }
    protected InstrumentModel Instrument { get; set; } = new InstrumentModel
    {
      Name = "DOGE/USD",
      Type = InstrumentEnum.Coins,
      TimeFrame = TimeSpan.FromMinutes(1)
    };

    protected override async Task OnAfterRenderAsync(bool setup)
    {
      if (setup)
      {
        await CreateViews();

        View.OnPreConnect = CreateAccounts;
        View.OnPostConnect = () =>
        {
          var account = View.Adapters["Prime"].Account;

          View.DealsView.UpdateItems(account.Deals);
          View.OrdersView.UpdateItems(account.Orders.Values);
          View.PositionsView.UpdateItems(account.Positions.Values);
        };
      }

      await base.OnAfterRenderAsync(setup);
    }

    protected virtual async Task CreateViews()
    {
      await View.ChartsView.Create("Prices");
      await View.ReportsView.Create("Performance");
    }

    protected virtual void CreateAccounts()
    {
      var account = new Account
      {
        Descriptor = "Demo",
        Instruments = new ConcurrentDictionary<string, InstrumentModel>
        {
          [Instrument.Name] = Instrument
        }
      };

      View.Adapters["Prime"] = new Adapter
      {
        Account = account,
        Source = Environments.Paper,
        ClientId = Configuration["Alpaca:PaperToken"],
        ClientSecret = Configuration["Alpaca:PaperSecret"]
      };

      Performance = new PerformanceIndicator { Name = "Balance" };

      View
        .Adapters
        .Values
        .ForEach(adapter => adapter.PointStream += async message =>
        {
          if (Equals(message.Next.Instrument.Name, Instrument.Name))
          {
            await OnData(message.Next);
          }
        });
    }

    protected async Task OnData(PointModel point)
    {
      var name = Instrument.Name.Replace("/", string.Empty);
      var account = View.Adapters["Prime"].Account;
      var instrument = account.Instruments[Instrument.Name];
      var performance = Performance.Calculate([account]);
      var openOrders = account.Orders.Values.Where(o => Equals(o.Name, name));
      var openPositions = account.Positions.Values.Where(o => Equals(o.Name, name));

      if (openOrders.IsEmpty() && openPositions.IsEmpty())
      {
        await OpenPositions(Instrument, 1);
        await TradeService.Done(async () =>
        {
          var position = account
            .Positions
            .Values
            .Where(o => Equals(o.Name, name))
            .FirstOrDefault();

          if (position is not null)
          {
            await ClosePositions(name);
          }

        }, 10000);
      }

      View.ChartsView.UpdateItems(point.Time.Value.Ticks, "Prices", "Bars", View.ChartsView.GetShape<CandleShape>(point));
      View.ReportsView.UpdateItems(point.Time.Value.Ticks, "Performance", "Balance", new AreaShape { Y = account.Balance });
      View.ReportsView.UpdateItems(point.Time.Value.Ticks, "Performance", "PnL", new LineShape { Y = performance.Point.Last });
      View.DealsView.UpdateItems(account.Deals);
      View.OrdersView.UpdateItems(account.Orders.Values);
      View.PositionsView.UpdateItems(account.Positions.Values);
    }

    protected double? GetPrice(double direction) => direction > 0 ?
      Instrument.Point.Ask :
      Instrument.Point.Bid;

    protected async Task OpenPositions(InstrumentModel instrument, double direction)
    {
      var adapter = View.Adapters["Prime"];
      var side = direction > 0 ? OrderSideEnum.Buy : OrderSideEnum.Sell;
      var stopSide = direction < 0 ? OrderSideEnum.Buy : OrderSideEnum.Sell;

      var TP = new OrderModel
      {
        Volume = 10,
        Side = stopSide,
        Type = OrderTypeEnum.Limit,
        Instruction = InstructionEnum.Brace,
        Price = GetPrice(direction) + 15 * direction,
        Transaction = new() { Instrument = instrument }
      };

      var SL = new OrderModel
      {
        Volume = 10,
        Side = stopSide,
        Type = OrderTypeEnum.Stop,
        Instruction = InstructionEnum.Brace,
        Price = GetPrice(-direction) - 15 * direction,
        Transaction = new() { Instrument = instrument }
      };

      var order = new OrderModel
      {
        Side = side,
        Volume = 10,
        Price = GetPrice(direction),
        Type = OrderTypeEnum.Market,
        Transaction = new() { Instrument = instrument }
        //Orders = [SL, TP]
      };

      await adapter.CreateOrders(order);
    }

    protected async Task ClosePositions(string name)
    {
      var adapter = View.Adapters["Prime"];

      foreach (var position in adapter.Account.Positions.Values.Where(o => Equals(name, o.Name)))
      {
        var side = position.Side is OrderSideEnum.Buy ? OrderSideEnum.Sell : OrderSideEnum.Buy;
        var order = new OrderModel
        {
          Side = side,
          Volume = position.Volume,
          Type = OrderTypeEnum.Market,
          Transaction = new()
          {
            Instrument = position.Transaction.Instrument
          }
        };

        await adapter.CreateOrders(order);
      }
    }
  }
}