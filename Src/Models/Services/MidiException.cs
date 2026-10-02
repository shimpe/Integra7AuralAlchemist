using System;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>A value that cannot become a valid INTEGRA-7 message or preset: a bank number the device
/// does not have, a tone type or bank name outside its vocabulary.</summary>
public class MidiException(string message) : Exception(message);
