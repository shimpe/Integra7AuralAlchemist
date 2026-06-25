using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Integra7AuralAlchemist.Models.Services;

namespace Integra7AuralAlchemist.ParameterGen;

public class Integra7ParameterDatabaseAnalyzer
{
    public static void CheckProgrammingErrorDuplicatePaths(IList<ParameterDef> database)
    {
        HashSet<string> PathsEncountered = [];
        var previousCommonPrefix = "";
        long prevAddress = 0;
        var prevParent = "";
        var prevParentVal = "";
        var prevParent2 = "";
        var prevParentVal2 = "";
        long prevBytes = 0;

        foreach (var s in database)
        {
            string[] el = s.Path.Split('/');
            foreach (var e in el)
            {
                if (e == "")
                {
                    Debug.WriteLine($"double slash found in {el}. Please fix.");
                }
                else
                {
                    if (e[0] == ' ' || e[^1] == ' ')
                        Debug.WriteLine($"Extra spaces found in path for {s.Path}. Please fix.");
                }

                if (s.ParentCtrl != "")
                    if (s.ParentCtrl[0] == ' ' || s.ParentCtrl[^1] == ' ')
                        Debug.WriteLine($"Extra spaces found in par:path for {s.Path}. Please fix.");

                if (s.ParentCtrl2 != "")
                    if (s.ParentCtrl2[0] == ' ' || s.ParentCtrl2[^1] == ' ')
                        Debug.WriteLine($"Extra spaces found in par2:path for {s.Path}. Please fix.");
            }

            if (PathsEncountered.Contains(s.Path))
                Debug.WriteLine($"Path {s.Path} is used multiple times. Please fix.");
            PathsEncountered.Add(s.Path);
            if (s.Path.Contains("Reserved") && !s.Reserved)
                Debug.WriteLine($"Path {s.Path} is named reserved but doesn't have Reserved flag. Please fix.");
            if (!s.Path.Contains("Reserved") && s.Reserved)
                Debug.WriteLine(
                    $"Path {s.Path} probably shouldn't have its Reserved flag turned on (otherwise, use Reserved in its name). Please fix.");
            if (s.ParentCtrl != "")
                if (!PathsEncountered.Contains(s.ParentCtrl))
                    Debug.WriteLine(
                        $"Path {s.Path} refers to a non-existing par:{s.ParentCtrl}. Please fix. Parameters can only depend on parameters that came before them.");

            if (s.ParentCtrl2 != "")
                if (!PathsEncountered.Contains(s.ParentCtrl2))
                    Debug.WriteLine(
                        $"Path {s.Path} refers to a non-existing par2:{s.ParentCtrl2}. Please fix. Parameters can only depend on parameters that came before them.");

            if (previousCommonPrefix != "")
            {
                var noOfSlash = s.Path.Count(c => c == '/');
                var newCommonPrefix = string.Join("/", s.Path.Split("/")[..noOfSlash]);
                if (newCommonPrefix == previousCommonPrefix)
                {
                    var newAddress = ByteUtils.Bytes7ToInt(s.Address);
                    if ((newAddress <= prevAddress && s.ParentCtrl == "") ||
                        (newAddress < prevAddress && s.ParentCtrl != ""))
                        Debug.WriteLine($"Successive offsets/addresses should increase at {s.Path}. Please check.");
                    if (s.ParentCtrl == prevParent && s.ParentCtrlDispValue == prevParentVal &&
                        s.ParentCtrl2 == prevParent2 && s.ParentCtrlDispValue2 == prevParentVal2)
                    {
                        if (prevParent != "" && prevParent2 == "")
                            Debug.WriteLine(
                                $"{s.Path}: No two parameters should have exact same par:{prevParent}, parval:{prevParentVal}. Please fix.");
                        if (prevParent != "" && prevParent2 != "")
                            Debug.WriteLine(
                                $"{s.Path}: No two parameters should have exact same par:{prevParent}, parval:{prevParentVal}, par2:{prevParent2}, parval2:{prevParentVal2}. Please fix.");
                    }

                    if (ByteUtils.Bytes7ToInt(s.Address) != prevAddress + prevBytes)
                        if (prevParentVal == s.ParentCtrlDispValue)
                            Debug.WriteLine(
                                $"{s.Path}: something seems fishy with the offset address. It doesn't correspond to previous address + previous #bytes). Please check.");
                    previousCommonPrefix = newCommonPrefix;
                    prevAddress = newAddress;
                    prevParent = s.ParentCtrl;
                    prevParentVal = s.ParentCtrlDispValue;
                    prevParent2 = s.ParentCtrl2;
                    prevParentVal2 = s.ParentCtrlDispValue2;
                    prevBytes = s.Bytes;
                }
                else
                {
                    var noOfSlash2 = s.Path.Count(c => c == '/');
                    previousCommonPrefix = string.Join("/", s.Path.Split("/")[..noOfSlash2]);
                    var address = ByteUtils.Bytes7ToInt(s.Address);
                    prevAddress = address;
                    prevParent = s.ParentCtrl;
                    prevParentVal = s.ParentCtrlDispValue;
                    prevParent2 = s.ParentCtrl2;
                    prevParentVal2 = s.ParentCtrlDispValue2;
                    prevBytes = s.Bytes;
                }
            }
            else
            {
                var noOfSlash = s.Path.Count(c => c == '/');
                previousCommonPrefix = string.Join("/", s.Path.Split("/")[..noOfSlash]);
                var address = ByteUtils.Bytes7ToInt(s.Address);
                prevAddress = address;
                prevParent = s.ParentCtrl;
                prevParentVal = s.ParentCtrlDispValue;
                prevParent2 = s.ParentCtrl2;
                prevParentVal2 = s.ParentCtrlDispValue2;
                prevBytes = s.Bytes;
            }

            if (!s.Reserved)
                if (s.OMin == -20000) // generic parameter
                    if (s.Repr == null) // no repr to determine ui limits
                        if (float.IsNaN(s.IMin2)) // no omin2 to determine ui limit
                            Debug.WriteLine(
                                $"{s.Path} does not specify a usable ui limit. Please add imin2, imax2, omin2, omax2 or repr");
        }
    }

    public static void MarkAllParentParametersAsIsParentTrue(IList<ParameterDef> database)
    {
        HashSet<string> ParametersRequiringIsParentTrue = [];
        // pass one: collect all parent parameters
        foreach (var s in database)
        {
            if (s.ParentCtrl != "") ParametersRequiringIsParentTrue.Add(s.ParentCtrl);
            if (s.ParentCtrl2 != "") ParametersRequiringIsParentTrue.Add(s.ParentCtrl2);
        }

        // pass two: mark all parent parameters as parent parameters
        foreach (var s in database)
            if (ParametersRequiringIsParentTrue.Contains(s.Path))
                s.IsParent = true;
    }

    public static void FillInSecondaryDependencies(IList<ParameterDef> database)
    {
        IDictionary<string, Tuple<string, string>> ParametersDependingOnOtherParameter =
            new Dictionary<string, Tuple<string, string>>();
        // pass one: collect all parameters that depend on another parameter
        foreach (var s in database)
        {
            if (s.ParentCtrl != "")
                ParametersDependingOnOtherParameter[s.Path] =
                    new Tuple<string, string>(s.ParentCtrl, s.ParentCtrlDispValue);
            if (s.ParentCtrl2 != "")
                ParametersDependingOnOtherParameter[s.Path] =
                    new Tuple<string, string>(s.ParentCtrl2, s.ParentCtrlDispValue2);
        }

        // pass two, for each parameter taht depends on another parameter, check if that other parameter in turn also depends on another parameter
        IList<Tuple<string, Tuple<string, string>, Tuple<string, string>>> twoLevelDep = [];
        foreach (var a in ParametersDependingOnOtherParameter.Keys)
        {
            Tuple<string, string> b = ParametersDependingOnOtherParameter[a];
            if (ParametersDependingOnOtherParameter.ContainsKey(b.Item1))
            {
                // a depends on b, and b depends on c
                Tuple<string, string> c = ParametersDependingOnOtherParameter[b.Item1];
                twoLevelDep.Add(new Tuple<string, Tuple<string, string>, Tuple<string, string>>(
                    a,
                    new Tuple<string, string>(b.Item1, b.Item2),
                    new Tuple<string, string>(c.Item1, c.Item2)));
                //Debug.WriteLine($"{a} depends on {b.Item1}[{b.Item2}] and {c.Item1}[{c.Item2}]");
                if (ParametersDependingOnOtherParameter.ContainsKey(c.Item1))
                    Debug.Assert(false, "3-level deep dependencies not supported!");
            }
        }

        // pass three, update database with the two level dependencies
        foreach (var s in database)
        foreach (Tuple<string, Tuple<string, string>, Tuple<string, string>> abc in twoLevelDep)
            if (s.Path == abc.Item1)
            {
                var b = abc.Item2.Item1;
                var b_disp = abc.Item2.Item2;
                var c = abc.Item3.Item1;
                var c_disp = abc.Item3.Item2;
                s.ParentCtrl = c;
                s.ParentCtrlDispValue = c_disp;
                s.ParentCtrl2 = b;
                s.ParentCtrlDispValue2 = b_disp;
            }
    }
}