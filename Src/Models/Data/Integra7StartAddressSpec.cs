using System.ComponentModel;

namespace Integra7AuralAlchemist.Models.Data;

public class Integra7StartAddressSpec 
{
    private byte[] _addr;
    public byte[] Address { get => _addr; }

    public Integra7StartAddressSpec(byte[] addr)
    {
        _addr = addr;
    }
}