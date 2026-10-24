using Polifemo.Contracts.Errors;

namespace Polifemo.Cli;

/// <summary>Arguments that parse but make no sense (for example a missing directory). Exit code 2.</summary>
public sealed class UsageException(string message) : PolifemoException("USAGE", message);
