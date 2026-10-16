using IDS4ALL.Core.Model;
using Python.Runtime;

namespace IDS4ALL.IfcTester;

/// <summary>Converts the IDS records into ifctester objects and writes the IDS file.</summary>
public sealed class IdsWriter
{
    // Same code as add_comment_to_xml in custom_functions.py.
    private const string CommentModule = """
        from xml.etree import ElementTree as ET

        def add_comment_to_xml(file_path, comment_text):
            tree = ET.parse(file_path)
            root = tree.getroot()
            comment = ET.Comment(comment_text)
            root.insert(0, comment)
            with open(file_path, "wb") as f:
                f.write(b'<?xml version="1.0" encoding="utf-8"?>\n')
                f.write(ET.tostring(root, encoding="utf-8", method="xml").replace(b'-->', b'-->\n\t'))
        """;

    public IdsWriter(PythonRuntimeOptions options) => PythonRuntime.EnsureInitialized(options);

    /// <summary>Writes the IDS file. Adds the comment as first child of the root when one is given.</summary>
    /// <returns>True when the file is valid against the IDS schema (result of ifctester to_xml).</returns>
    /// <exception cref="IdsWriteException">ifctester raised an exception.</exception>
    public bool Write(IdsFile file, string path, string? creatorComment = null)
    {
        using (Py.GIL())
        {
            try
            {
                using var ids = Py.Import("ifctester.ids");
                using var document = Build(ids, file);
                using var result = document.InvokeMethod("to_xml", new PyString(path));
                var isValid = result.IsTrue();
                if (creatorComment is not null) AddComment(path, creatorComment);
                return isValid;
            }
            catch (PythonException ex)
            {
                throw new IdsWriteException(ex.Value?.ToString() ?? ex.Message, ex);
            }
        }
    }

    private static PyObject Build(PyObject ids, IdsFile file)
    {
        var document = Call(ids, "Ids",
            ("title", file.Title), ("copyright", file.Copyright), ("version", file.Version),
            ("description", file.Description), ("author", file.Author), ("date", file.Date),
            ("purpose", file.Purpose), ("milestone", file.Milestone));
        using var specifications = document.GetAttr("specifications");
        foreach (var specification in file.Specifications)
        {
            using var built = BuildSpecification(ids, specification);
            specifications.InvokeMethod("append", built).Dispose();
        }
        return document;
    }

    private static PyObject BuildSpecification(PyObject ids, Specification spec)
    {
        var built = Call(ids, "Specification",
            ("name", spec.Name), ("ifcVersion", spec.IfcVersions), ("minOccurs", spec.MinOccurs),
            ("maxOccurs", spec.MaxOccurs is { } max ? max : "unbounded"), ("instructions", spec.Instructions));
        AppendFacets(ids, built.GetAttr("applicability"), spec.Applicability);
        AppendFacets(ids, built.GetAttr("requirements"), spec.Requirements);
        return built;
    }

    private static void AppendFacets(PyObject ids, PyObject target, IReadOnlyList<Facet> facets)
    {
        using (target)
        {
            foreach (var facet in facets)
            {
                using var built = BuildFacet(ids, facet);
                target.InvokeMethod("append", built).Dispose();
            }
        }
    }

    private static PyObject BuildFacet(PyObject ids, Facet facet) => facet switch
    {
        EntityFacet f => Call(ids, "Entity",
            ("name", f.Name), ("predefinedType", f.PredefinedType), ("instructions", f.Instructions)),
        PropertyFacet f => Call(ids, "Property",
            ("propertySet", f.PropertySet), ("baseName", f.BaseName), ("value", f.Value), ("dataType", f.DataType),
            ("uri", f.Uri), ("cardinality", f.Cardinality), ("instructions", f.Instructions)),
        AttributeFacet f => Call(ids, "Attribute",
            ("name", f.Name), ("value", f.Value), ("cardinality", f.Cardinality)),
        ClassificationFacet f => Call(ids, "Classification",
            ("value", f.Value), ("system", f.System), ("uri", f.Uri), ("cardinality", f.Cardinality)),
        PartOfFacet f => Call(ids, "PartOf",
            ("name", f.Name), ("predefinedType", f.PredefinedType), ("relation", f.Relation),
            ("cardinality", f.Cardinality)),
        MaterialFacet f => Call(ids, "Material",
            ("value", f.Value), ("uri", f.Uri), ("cardinality", f.Cardinality)),
        _ => throw new ArgumentOutOfRangeException(nameof(facet), facet, "Unknown facet type."),
    };

    private static void AddComment(string path, string comment)
    {
        using var scope = Py.CreateScope();
        scope.Exec(CommentModule);
        using var function = scope.Get("add_comment_to_xml");
        function.Invoke(new PyString(path), new PyString(comment)).Dispose();
    }

    /// <summary>Calls ids.&lt;name&gt;(**kwargs). A null argument becomes None.</summary>
    private static PyObject Call(PyObject module, string name, params (string Key, object? Value)[] arguments)
    {
        using var type = module.GetAttr(name);
        using var kwargs = new PyDict();
        var owned = new List<PyObject>();
        try
        {
            foreach (var (key, value) in arguments)
            {
                var python = ToPython(module, value);
                owned.Add(python);
                kwargs.SetItem(key, python);
            }
            using var noArguments = new PyTuple();
            return type.Invoke(noArguments, kwargs);
        }
        finally
        {
            foreach (var item in owned) item.Dispose();
        }
    }

    private static PyObject ToPython(PyObject ids, object? value) => value switch
    {
        null => PyObject.None,
        PyObject existing => existing,
        string text => new PyString(text),
        int number => new PyInt(number),
        double number => new PyFloat(number),
        IReadOnlyList<string> list => ToList(list),
        SimpleValue simple => new PyString(simple.Text),
        EnumerationRestriction r => Restriction(ids, r.Base, "enumeration", ToList(r.Options)),
        PatternRestriction r => Restriction(ids, r.Base, "pattern", new PyString(r.Pattern)),
        BoundRestriction r => Restriction(ids, r.Base, r.Kind.ToString().ToLowerFirst(), new PyFloat(r.Bound)),
        LengthRestriction r => Restriction(ids, r.Base, r.Kind.ToString().ToLowerFirst(), new PyInt(r.Length)),
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Cannot convert the value to Python."),
    };

    private static PyList ToList(IEnumerable<string> items)
    {
        var list = new PyList();
        foreach (var item in items)
        {
            using var text = new PyString(item);
            list.Append(text);
        }
        return list;
    }

    private static PyObject Restriction(PyObject ids, string restrictionBase, string option, PyObject optionValue)
    {
        using (optionValue)
        using (var options = new PyDict())
        {
            options.SetItem(option, optionValue);
            return Call(ids, "Restriction", ("options", options), ("base", restrictionBase));
        }
    }
}

internal static class StringExtensions
{
    public static string ToLowerFirst(this string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];
}
