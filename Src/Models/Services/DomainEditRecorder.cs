using System.Linq;
using Integra7AuralAlchemist.Models.Domain;
using Serilog;

namespace Integra7AuralAlchemist.Models.Services;

/// <summary>
/// Recording for a write door that speaks in domains and paths rather than in
/// <c>FullyQualifiedParameter</c>s.
///
/// The two shipped doors both hold the parameter object itself and can read everything a
/// <see cref="ParameterChange"/> needs straight off it -- <c>SynthParam</c>'s wrappers from their
/// <c>_p</c>, the raw grid from the <c>UpdateMessageSpec</c>'s <c>Par</c>. The Motional Surround editor
/// is the odd one out: it writes through <c>DomainBase.WriteToIntegraAsync(path, displayValue)</c> on a
/// debounced subject, so all it has at the point of the edit is a block and a path. This derives the rest
/// from those two, which is the part that can be wrong and therefore the part worth testing:
///
/// <list type="bullet">
/// <item>The <b>old value</b> comes from the block, not from the caller. Only
/// <c>WriteToIntegraAsync</c> -&gt; <c>ModifySingleParameterDisplayedValue</c> replaces it, so a caller
/// that records before it enqueues its (debounced, and in any case deferred) write still reads the value
/// from before the edit. Recording after the write would record the new value as the old one and undo
/// would be a no-op.</item>
/// <item>The <b>discriminator flag</b> is read off <c>ParSpec.IsParent</c> of the live parameter rather
/// than assumed. Nothing in either Motional Surround block is a discriminator today, but a hard-coded
/// <c>false</c> would silently break the write ordering and the dependent resync (see
/// <see cref="PendingEdit.Writes"/>) the day one appears -- or the day this helper is reused by another
/// door.</item>
/// </list>
///
/// No Avalonia and no device, so it is unit-testable against a real parameter database.
/// </summary>
public static class DomainEditRecorder
{
    /// <summary>What recording this edit would put in the journal, or null when there is nothing to
    /// record: no such parameter in the block, or one that is not valid in the block's current context
    /// and whose write <c>ModifySingleParameterDisplayedValue</c> would therefore skip anyway. Separate
    /// from <see cref="Record"/> so the derivation can be tested without the process-wide journal.</summary>
    public static ParameterChange? Describe(DomainBase domain, string path, string newDisplayValue)
    {
        // GetRelevantParameters(true, true) -- reserved and context-invalid included -- so this finds the
        // parameter whenever the block has one at all, and the context question is answered once, below,
        // by the same lookup the write itself will use.
        var p = domain.GetRelevantParameters(true, true).FirstOrDefault(x => x.ParSpec.Path == path);
        if (p is null)
        {
            Log.Error("Not recording an edit to '{Path}': no such parameter in the block " +
                      "(\"{Start}\", \"{Offset}\", \"{Offset2}\").",
                path, domain.StartAddressName, domain.OffsetAddressName, domain.Offset2AddressName);
            return null;
        }

        var oldValue = domain.LookupSingleParameterDisplayedValue(path);
        if (oldValue.Length == 0)
        {
            // Two ways to get here, and undo can do nothing useful with either. The parameter is not
            // ValidInContext, so the lookup refused it (and logged that itself) and the write about to be
            // enqueued would be skipped by the same test. Or the block has never been read -- a
            // FullyQualifiedParameter starts out with an empty StringValue -- so there is no value from
            // before the edit to put back. No real display value is empty.
            Log.Error("Not recording an edit to '{Path}': it has no value to go back to (never read, or " +
                      "not valid in the block's current context).", path);
            return null;
        }

        return new ParameterChange(
            Start: domain.StartAddressName, Offset: domain.OffsetAddressName,
            Offset2: domain.Offset2AddressName, Path: path,
            OldValue: oldValue, NewValue: newDisplayValue,
            IsDiscriminator: p.ParSpec.IsParent);
    }

    /// <summary>Record this edit in the journal the application undoes from. Call it from inside whatever
    /// guard distinguishes a user edit from a device echo, and <em>before</em> the write -- see the class
    /// remarks for both.</summary>
    public static void Record(DomainBase domain, string path, string newDisplayValue)
    {
        if (Describe(domain, path, newDisplayValue) is { } change) EditJournal.Default.Record(change);
    }
}
