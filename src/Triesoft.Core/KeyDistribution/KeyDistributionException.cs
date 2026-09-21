namespace Triesoft.Core.KeyDistribution;

/// <summary>Paket kunci ditolak: rusak, dipalsukan, bukan untuk mesin ini, atau dari penerbit yang tidak dipercaya.</summary>
public sealed class KeyDistributionException(string message, Exception? inner = null) : Exception(message, inner);
