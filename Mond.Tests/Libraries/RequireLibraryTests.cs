﻿using System.IO;
using Mond.Libraries;
using NUnit.Framework;

namespace Mond.Tests.Libraries
{
    [TestFixture]
    public class RequireLibraryTests
    {
        [Test]
        public void Require()
        {
            var result = RunModule(
                "return require('module').foo;",
                "module",
                "exports.foo = 'bar';"
            );
            
            Assert.AreEqual((MondValue)"bar", result);
        }

        [Test]
        public void Import()
        {
            const string mainScript =
                """
                import Module;
                return Module.method();
                """;
            const string moduleScript =
                """
                export fun method() {
                    return 10;
                }
                """;
            var result = RunModule(mainScript, "Module", moduleScript);
            Assert.AreEqual((MondValue)10, result);
        }

        [Test]
        public void ImportInvalidModuleName()
        {
            const string mainScript =
                """
                import module;
                return module.method();
                """;
            const string moduleScript =
                """
                export fun method() {
                    return 10;
                }
                """;
            Assert.Throws<MondCompilerException>(() => RunModule(mainScript, "module", moduleScript));
        }

        [Test]
        public void ImportInvalidIdentifier()
        {
            const string mainScript =
                """
                import '123';
                """;
            const string moduleScript =
                """
                export fun method() {
                    return 10;
                }
                """;
            Assert.Throws<MondCompilerException>(() => RunModule(mainScript, "123", moduleScript));
        }

        [Test]
        public void ImportString()
        {
            const string mainScript =
                """
                import 'Module.mnd';
                return Module.method();
                """;
            const string moduleScript =
                """
                export fun method() {
                    return 10;
                }
                """;
            var result = RunModule(mainScript, "Module.mnd", moduleScript);
            Assert.AreEqual((MondValue)10, result);
        }

        [Test]
        public void ImportDestructured()
        {
            const string mainScript =
                """
                from Module import { method };
                return method();
                """;
            const string moduleScript =
                """
                export fun method() {
                    return 10;
                }
                """;
            var result = RunModule(mainScript, "Module", moduleScript);
            Assert.AreEqual((MondValue)10, result);
        }

        [Test]
        public void ImportStringDestructured()
        {
            const string mainScript =
                """
                from 'module.mnd' import { method };
                return method();
                """;
            const string moduleScript =
                """
                export fun method() {
                    return 10;
                }
                """;
            var result = RunModule(mainScript, "module.mnd", moduleScript);
            Assert.AreEqual((MondValue)10, result);
        }

        [Test]
        public void ExportAll()
        {
            const string mainScript =
                """
                return fun (exports) {
                    export * from 'module.mnd';
                };
                """;
            const string moduleScript =
                """
                export fun methodA() {
                    return 10;
                }
                export fun methodB() {
                    return 20;
                }
                """;
            var module = RunModule(mainScript, "module.mnd", moduleScript, out var state);
            var exports = MondValue.Object(state);
            state.Call(module, exports);

            var methodA = exports["methodA"];
            Assert.AreEqual(MondValueType.Function, methodA.Type);
            var resultA = state.Call(methodA);
            Assert.AreEqual((MondValue)10, resultA);

            var methodB = exports["methodB"];
            Assert.AreEqual(MondValueType.Function, methodB.Type);
            var resultB = state.Call(methodB);
            Assert.AreEqual((MondValue)20, resultB);
        }

        private static MondValue RunModule(string mainScript, string moduleName, string moduleScript)
        {
            return RunModule(mainScript, moduleName, moduleScript, out _);
        }

        private static MondValue RunModule(string mainScript, string moduleName, string moduleScript, out MondState state)
        {
            const string mainPath = "/test/main.mnd";
            var searchPath = Path.GetDirectoryName(mainPath);

            var configured = false;
            state = new MondState
            {
                Options =
                {
                    DebugInfo = MondDebugInfoLevel.Full,
                },
            };

            state.Libraries.Configure(libraries =>
            {
                var requireLibrary = libraries.Get<RequireLibrary>();
                Assert.IsNotNull(requireLibrary);

                requireLibrary.Resolver = (name, directories) =>
                {
                    Assert.AreEqual(moduleName, name);
                    CollectionAssert.Contains(directories, searchPath);

                    return "resolved-module";
                };

                requireLibrary.Loader = resolvedName =>
                {
                    Assert.AreEqual("resolved-module", resolvedName);
                    return moduleScript;
                };

                configured = true;
            });

            var result = state.Run(mainScript, mainPath);
            Assert.IsTrue(configured, "Configure was not called");
            return result;
        }
    }
}
