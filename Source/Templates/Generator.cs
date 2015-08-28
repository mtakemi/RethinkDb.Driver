using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Humanizer;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Templates.CodeGen;
using Templates.CodeGen.Specialized;
using Templates.Metadata;

namespace Templates
{
    [TestFixture]
    public class Generator
    {
        public string ProjectFolder = "RethinkDb.Driver";
        public string GenerateRootDir = @"./Generated";
        public string ProtoDir = @"./Generated/Proto";
        public string AstClasses = @"./Generated/Ast";
        public string ModelDir = @"./Generated/Model";

        [TestFixtureSetUp]
        public void BeforeRunningTestSession()
        {
            MetaDb.Initialize(@"..\..\Metadata");

            //remount the working directory before we begin.
            var rootProjectPath = Path.Combine(Directory.GetCurrentDirectory(), @"..\..\..", ProjectFolder);
            Directory.SetCurrentDirectory(rootProjectPath);
            EnsurePathsExist();
        }

        private void Clean()
        {
            if( Directory.Exists(GenerateRootDir) )
            {
                Directory.Delete(GenerateRootDir, true);
            }
        }

        private void EnsurePathsExist()
        {
            if( !Directory.Exists(GenerateRootDir) )
                Directory.CreateDirectory(GenerateRootDir);
            if( !Directory.Exists(ProtoDir) )
                Directory.CreateDirectory(ProtoDir);
            if( !Directory.Exists(AstClasses) )
                Directory.CreateDirectory(AstClasses);
            if( !Directory.Exists(ModelDir) )
                Directory.CreateDirectory(ModelDir);
        }

        [Test]
        [Explicit]
        public void Generate_All()
        {
            Clean();
            EnsurePathsExist();
            Render_Proto_Enums();
            Render_Ast_SubClasses();
            Render_Global_Options();
            Render_Exceptions();
            Render_OptArg_Enums();
        }

        [Test]
        public void Render_Proto_Enums()
        {
            RenderEnum("Version", MetaDb.Protocol["VersionDummy"]["Version"].ToObject<Dictionary<string, object>>());
            RenderEnum("Protocol", MetaDb.Protocol["VersionDummy"]["Protocol"].ToObject<Dictionary<string, object>>());
            RenderEnum("QueryType", MetaDb.Protocol["Query"]["QueryType"].ToObject<Dictionary<string, object>>());
            RenderEnum("FrameType", MetaDb.Protocol["Frame"]["FrameType"].ToObject<Dictionary<string, object>>());
            RenderEnum("ResponseType", MetaDb.Protocol["Response"]["ResponseType"].ToObject<Dictionary<string, object>>());
            RenderEnum("ResponseNote", MetaDb.Protocol["Response"]["ResponseNote"].ToObject<Dictionary<string, object>>());
            RenderEnum("ErrorType", MetaDb.Protocol["Response"]["ErrorType"].ToObject<Dictionary<string, object>>());
            RenderEnum("DatumType", MetaDb.Protocol["Datum"]["DatumType"].ToObject<Dictionary<string, object>>());
            RenderEnum("TermType", MetaDb.Protocol["Term"]["TermType"].ToObject<Dictionary<string, object>>());
        }

        [Test]
        public void Render_Ast_SubClasses()
        {
            var speicalSuperclasses = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    {"DB", "ReqlAst"},
                    {"ReqlQuery", "ReqlAst"},
                    {"TopLevel", "ReqlAst"},
                };

            var terms = MetaDb.TermInfo.ToObject<Dictionary<string, JObject>>();
            EnsureLanguageSafeTerms(terms);

            RenderAstSubclass(null, "ReqlQuery", speicalSuperclasses["ReqlQuery"], "T_EXPR", terms);
            RenderAstSubclass(null, "TopLevel", speicalSuperclasses["TopLevel"], "T_TOP_LEVEL", terms);

            foreach( var kvp in terms )
            {
                if( !kvp.Value["deprecated"]?.ToObject<bool?>() ?? true )
                {
                    var termName = kvp.Key;
                    string superClass = null;
                    speicalSuperclasses.TryGetValue(termName, out superClass);
                    RenderAstSubclass(termName, null, superClass ?? "ReqlQuery", termName.ToLower(), terms);
                }
                else
                {
                    Console.WriteLine("Deprcated:" + kvp.Key);
                }
            }
        }

        [Test]
        public void Render_Global_Options()
        {
            var optArgs = MetaDb.Global["global_optargs"].ToObject<Dictionary<string, string>>();

            var tmpl = new GlobalOptionsTemplate()
                {
                    OptArgs = optArgs
                };

            File.WriteAllText(Path.Combine(ModelDir, "GlobalOptions.cs"), tmpl.TransformText());
        }

        [Test]
        public void Render_Exceptions()
        {
            var errorHearchy = MetaDb.Global["exception_hierarchy"] as JObject;
            RenderExceptions(errorHearchy);
        }

        [Test]
        public void Render_OptArg_Enums()
        {
            var optArgs = MetaDb.Global["optarg_enums"].ToObject<Dictionary<string, string[]>>();

            foreach( var kvp in optArgs )
            {
                var enumName = kvp.Key.Substring(2).ToLower().Pascalize();
                var values = kvp.Value;
                RenderEnumString(enumName, values);
            }
        }


        private void RenderExceptions(JObject error, string superClass = "Exception")
        {
            foreach( var p in error.Properties() )
            {
                RenderErrorClass(p.Name, superClass);
                RenderExceptions(p.Value as JObject, p.Name);
            }
        }
        
        public void RenderErrorClass(string className, string superClass)
        {
            var tmpl = new ExceptionTemplate()
                {
                    ClassName = className,
                    SuperClass = superClass
                };

            File.WriteAllText(Path.Combine(GenerateRootDir, $"{className.Pascalize()}.cs"), tmpl.TransformText());
        }

        public void RenderAstSubclass(string termType, string className, string superClass, string includeIn, Dictionary<string, JObject> meta)
        {
            className = className ?? termType.ToLower();

            var tmpl = GetSpeicalizedTemplate<AstSubclassTemplate>(className) ?? new AstSubclassTemplate();

            tmpl.TermType = termType;
            tmpl.ClassName = className;
            tmpl.Superclass = superClass;
            tmpl.IncludeIn = includeIn;
            tmpl.Meta = meta;

            File.WriteAllText(Path.Combine(AstClasses, $"{className.Pascalize()}.cs"), tmpl.TransformText());
        }

        public T GetSpeicalizedTemplate<T>(string className)
        {
            var type = typeof(MakeObj);
            var path = type.FullName.Replace(type.Name, "");
            var lookingFor = $"{path}{className.Pascalize()}";

            var specialType = Assembly.GetExecutingAssembly().GetType(lookingFor, false, true);
            if( specialType != null )
            {
                Console.WriteLine("Using Speical Template: " + lookingFor);
                return (T)Activator.CreateInstance(specialType);
            }

            return default(T);
        }


        public void RenderEnum(string enumName, Dictionary<string, object> enums )
        {
            var tmpl = GetSpeicalizedTemplate<EnumTemplate>(enumName) ?? new EnumTemplate();

            tmpl.EnumName = enumName;
            tmpl.Enums = enums;

            File.WriteAllText(Path.Combine(ProtoDir, $"{enumName.Pascalize()}.cs"), tmpl.TransformText());
        }
        public void RenderEnumString(string enumName, string[] enums)
        {
            var tmpl = GetSpeicalizedTemplate<EnumStringTemplate>(enumName) ?? new EnumStringTemplate();

            tmpl.EnumName = enumName;
            tmpl.Enums = enums;

            File.WriteAllText(Path.Combine(ModelDir, $"{enumName.Pascalize()}.cs"), tmpl.TransformText());
        }


        private void EnsureLanguageSafeTerms(Dictionary<string,JObject> terms)
        {
            var reservedWords = new[]
                {
                    "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
                    "checked", "class", "const", "continue", "decimal", "default", "delegate",
                    "do", "double", "else", "enum", "event", "explicit", "extern", "false",
                    "finally", "fixed", "float", "for", "forech", "goto", "if", "implicit",
                    "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
                    "new", "null", "object", "operator", "out", "override", "params", "private",
                    "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
                    "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw",
                    "true", "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort",
                    "using", "virtual", "volatile", "void", "while",
                };

            foreach( var kvp in terms )
            {
                var termName = kvp.Key;
                var info = kvp.Value;
                if( reservedWords.Contains(termName.ToLower()) )
                {
                    var alias = termName.ToLower() + "_";
                    Console.WriteLine($"Alias for {termName} will be {alias}");
                    info["sharp_alias"] = alias;
                }
            }

        }
    }
}