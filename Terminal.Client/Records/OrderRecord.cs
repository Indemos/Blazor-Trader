using System;
using Terminal.Core.EnumSpace;

namespace Terminal.Client.Records
{
  /// <summary>
  /// Template record
  /// </summary>
  public struct OrderRecord
  {
    public string Name { get; set; }
    public double Size { get; set; }
    public double Price { get; set; }
    public DateTime? Time { get; set; }
    public OrderSideEnum? Side { get; set; }
  }
}
