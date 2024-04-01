using System.Collections.ObjectModel;
using Terminal.Core.Models;

namespace Terminal.Core.Domains
{
  /// <summary>
  /// Definition
  /// </summary>
  public interface IIndicator<T>
  {
    /// <summary>
    /// Name
    /// </summary>
    string Name { get; set; }

    /// <summary>
    /// Point
    /// </summary>
    PointModel? Point { get; set; }

    /// <summary>
    /// Calculate indicator values
    /// </summary>
    /// <param name="collection"></param>
    /// <returns></returns>
    T Calculate(ObservableCollection<PointModel?> collection);
  }

  /// <summary>
  /// Implementation
  /// </summary>
  public class Indicator<T> : IIndicator<T>
  {
    /// <summary>
    /// Name
    /// </summary>
    public virtual string Name { get; set; }

    /// <summary>
    /// Point
    /// </summary>
    public virtual PointModel? Point { get; set; }

    /// <summary>
    /// Constructor
    /// </summary>
    public Indicator()
    {
      Point = new();
    }

    /// <summary>
    /// Calculate indicator values
    /// </summary>
    /// <param name="collection"></param>
    public virtual T Calculate(ObservableCollection<PointModel?> collection) => default;
  }
}
