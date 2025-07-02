using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PeppolSG.API.Tests
{
    [TestClass]
    public class ComplianceViolationTests
    {
        private readonly string _projectRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", ".."));
        private readonly List<string> _csharpFiles = new List<string>();
        private readonly List<string> _configFiles = new List<string>();
        private readonly List<string> _projectFiles = new List<string>();

        [TestInitialize]
        public void Setup()
        {
            DiscoverProjectFiles();
        }

        private void DiscoverProjectFiles()
        {
            var peppolApiPath = Path.Combine(_projectRoot, "PeppolSG.API");
            
            if (Directory.Exists(peppolApiPath))
            {
                _csharpFiles.AddRange(Directory.GetFiles(peppolApiPath, "*.cs", SearchOption.AllDirectories));
                _configFiles.AddRange(Directory.GetFiles(peppolApiPath, "*.config", SearchOption.AllDirectories));
                _projectFiles.AddRange(Directory.GetFiles(peppolApiPath, "*.csproj", SearchOption.AllDirectories));
            }
        }

        [TestMethod]
        public void Test_NoCSharp8PlusFeatures_Violation()
        {
            // This test will FAIL if C# 8.0+ features are found
            var violations = new List<string>();

            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                
                // C# 8.0+ features that should NOT exist
                if (content.Contains("switch expression") || content.Contains("switch =>"))
                {
                    violations.Add($"C# 8.0 switch expression found in {file}");
                }

                if (content.Contains("using declaration") || Regex.IsMatch(content, @"using\s+\w+\s*=\s*[^;]+;"))
                {
                    violations.Add($"C# 8.0 using declaration found in {file}");
                }

                if (content.Contains("nullable reference types") || content.Contains("#nullable"))
                {
                    violations.Add($"C# 8.0 nullable reference types found in {file}");
                }

                if (content.Contains("async foreach") || content.Contains("await foreach"))
                {
                    violations.Add($"C# 8.0 async foreach found in {file}");
                }

                // C# 9.0+ features
                if (content.Contains("record") && content.Contains("class"))
                {
                    violations.Add($"C# 9.0 record found in {file}");
                }

                if (content.Contains("init") && content.Contains("set"))
                {
                    violations.Add($"C# 9.0 init-only properties found in {file}");
                }

                // C# 10.0+ features
                if (content.Contains("global using"))
                {
                    violations.Add($"C# 10.0 global using found in {file}");
                }

                if (content.Contains("file-scoped namespace"))
                {
                    violations.Add($"C# 10.0 file-scoped namespace found in {file}");
                }
            }

            Assert.IsTrue(violations.Count == 0, 
                $"C# 7.3 compliance violations found:\n{string.Join("\n", violations)}");
        }

        [TestMethod]
        public void Test_NoDotNetCoreAPIs_Violation()
        {
            // This test will FAIL if .NET Core APIs are found
            var violations = new List<string>();

            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                
                // .NET Core/.NET 5+ specific APIs that don't exist in .NET Framework 4.8
                if (content.Contains("Microsoft.Extensions"))
                {
                    violations.Add($".NET Core Microsoft.Extensions found in {file}");
                }

                if (content.Contains("Microsoft.AspNetCore"))
                {
                    violations.Add($"ASP.NET Core references found in .NET Framework project: {file}");
                }
            }

            // Check project files for incompatible dependencies
            foreach (var projectFile in _projectFiles)
            {
                var content = File.ReadAllText(projectFile);
                
                if (content.Contains("System.Text.Json"))
                {
                    violations.Add($"Incompatible System.Text.Json dependency found in {projectFile}");
                }

                if (content.Contains("System.IO.Pipelines"))
                {
                    violations.Add($"Incompatible System.IO.Pipelines dependency found in {projectFile}");
                }

                if (content.Contains("System.Threading.Channels"))
                {
                    violations.Add($"Incompatible System.Threading.Channels dependency found in {projectFile}");
                }
            }

            Assert.IsTrue(violations.Count == 0, 
                $".NET Framework 4.8 compatibility violations found:\n{string.Join("\n", violations)}");
        }

        [TestMethod]
        public void Test_ProperControllerInheritance_Violation()
        {
            // This test will FAIL if controllers don't inherit properly
            var violations = new List<string>();

            foreach (var file in _csharpFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.EndsWith("Controller.cs")) continue;

                var content = File.ReadAllText(file);

                // Must inherit from Controller or ApiController
                if (!content.Contains("Controller") && !content.Contains("ApiController"))
                {
                    violations.Add($"Controller {fileName} does not inherit from Controller or ApiController");
                }

                // Check for proper action method signatures
                var actionMethods = Regex.Matches(content, @"public\s+(?:async\s+)?(?:Task<[^>]+>\s+)?\w+\s*\(");
                foreach (Match match in actionMethods)
                {
                    var methodContent = match.Value;
                    if (methodContent.Contains("async") && !methodContent.Contains("Task"))
                    {
                        violations.Add($"Async method without Task return type in {fileName}: {methodContent}");
                    }
                }
            }

            Assert.IsTrue(violations.Count == 0, 
                $"Controller inheritance violations found:\n{string.Join("\n", violations)}");
        }

        [TestMethod]
        public void Test_ProperAsyncAwaitUsage_Violation()
        {
            // This test will FAIL if async/await is used incorrectly
            var violations = new List<string>();

            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                var fileName = Path.GetFileName(file);

                // Check for async methods without proper return types
                var asyncMethods = Regex.Matches(content, @"public\s+async\s+([^{]+)");
                foreach (Match match in asyncMethods)
                {
                    var methodSignature = match.Value;
                    
                    // Async methods should return Task or Task<T>
                    if (!methodSignature.Contains("Task") && !methodSignature.Contains("void"))
                    {
                        violations.Add($"Async method without Task return type in {fileName}: {methodSignature}");
                    }
                }

                // Check for await without async
                var awaitStatements = Regex.Matches(content, @"\bawait\s+");
                if (awaitStatements.Count > 0)
                {
                    var methodMatches = Regex.Matches(content, @"\b(?:public|private|protected|internal)\s+(?:async\s+)?[^{]+{");
                    bool hasAsyncMethod = methodMatches.Cast<Match>().Any(m => m.Value.Contains("async"));
                    
                    if (!hasAsyncMethod)
                    {
                        violations.Add($"Await statements found without async method in {fileName}");
                    }
                }
            }

            Assert.IsTrue(violations.Count == 0, 
                $"Async/await usage violations found:\n{string.Join("\n", violations)}");
        }

        [TestMethod]
        public void Test_ProperUsingStatements_Violation()
        {
            // This test will FAIL if incompatible using statements are found
            var violations = new List<string>();

            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                var fileName = Path.GetFileName(file);

                // Check for incompatible using statements
                var usingStatements = Regex.Matches(content, @"using\s+([^;]+);");
                foreach (Match match in usingStatements)
                {
                    var usingStatement = match.Value;
                    
                    if (usingStatement.Contains("Microsoft.Extensions"))
                    {
                        violations.Add($"Incompatible using statement in {fileName}: {usingStatement}");
                    }

                    if (usingStatement.Contains("Microsoft.AspNetCore"))
                    {
                        violations.Add($"Incompatible using statement in {fileName}: {usingStatement}");
                    }
                }
            }

            Assert.IsTrue(violations.Count == 0, 
                $"Using statements violations found:\n{string.Join("\n", violations)}");
        }

        [TestMethod]
        public void Test_ProjectFileCompliance_Violation()
        {
            // This test will FAIL if project files have compliance issues
            var violations = new List<string>();

            foreach (var projectFile in _projectFiles)
            {
                var content = File.ReadAllText(projectFile);
                var fileName = Path.GetFileName(projectFile);

                // Check for correct target framework
                if (!content.Contains("TargetFrameworkVersion>v4.8</TargetFrameworkVersion>"))
                {
                    violations.Add($"Project file {fileName} does not target .NET Framework 4.8");
                }

                // Check for proper project type GUIDs
                if (!content.Contains("FAE04EC0-301F-11D3-BF4B-00C04F79EFBC"))
                {
                    violations.Add($"Project file {fileName} missing C# project type GUID");
                }

                // Check for incompatible dependencies
                if (content.Contains("System.Text.Json"))
                {
                    violations.Add($"Incompatible System.Text.Json dependency in {fileName}");
                }

                if (content.Contains("System.IO.Pipelines"))
                {
                    violations.Add($"Incompatible System.IO.Pipelines dependency in {fileName}");
                }
            }

            Assert.IsTrue(violations.Count == 0, 
                $"Project file compliance violations found:\n{string.Join("\n", violations)}");
        }

        [TestMethod]
        public void Test_WebConfigCompliance_Violation()
        {
            // This test will FAIL if Web.config has compliance issues
            var violations = new List<string>();

            foreach (var configFile in _configFiles)
            {
                var content = File.ReadAllText(configFile);
                var fileName = Path.GetFileName(configFile);

                if (fileName == "Web.config")
                {
                    // Check for required .NET Framework 4.8 configuration
                    if (!content.Contains("targetFramework=\"4.8\""))
                    {
                        violations.Add($"Web.config does not specify .NET Framework 4.8 target");
                    }

                    // Check for proper ASP.NET configuration
                    if (!content.Contains("<system.web>") && !content.Contains("<system.webServer>"))
                    {
                        violations.Add($"Web.config missing required system.web or system.webServer sections");
                    }
                }
            }

            Assert.IsTrue(violations.Count == 0, 
                $"Web.config compliance violations found:\n{string.Join("\n", violations)}");
        }

        [TestMethod]
        public void Test_OverallCompliance_Violation()
        {
            // This test will FAIL if ANY compliance violations are found
            var allViolations = new List<string>();

            // Run all compliance checks
            allViolations.AddRange(GetCSharpViolations());
            allViolations.AddRange(GetDotNetCoreViolations());
            allViolations.AddRange(GetControllerViolations());
            allViolations.AddRange(GetAsyncViolations());
            allViolations.AddRange(GetUsingViolations());
            allViolations.AddRange(GetProjectViolations());
            allViolations.AddRange(GetWebConfigViolations());

            Assert.IsTrue(allViolations.Count == 0, 
                $"OVERALL COMPLIANCE VIOLATIONS FOUND:\n{string.Join("\n", allViolations)}");
        }

        // Helper methods to collect violations
        private List<string> GetCSharpViolations()
        {
            var violations = new List<string>();
            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                if (content.Contains("switch expression") || content.Contains("switch =>"))
                    violations.Add($"C# 8.0+ feature in {file}");
                if (content.Contains("record") && content.Contains("class"))
                    violations.Add($"C# 9.0+ feature in {file}");
                if (content.Contains("global using"))
                    violations.Add($"C# 10.0+ feature in {file}");
            }
            return violations;
        }

        private List<string> GetDotNetCoreViolations()
        {
            var violations = new List<string>();
            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                if (content.Contains("Microsoft.Extensions"))
                    violations.Add($".NET Core API in {file}");
                if (content.Contains("Microsoft.AspNetCore"))
                    violations.Add($"ASP.NET Core API in {file}");
            }
            return violations;
        }

        private List<string> GetControllerViolations()
        {
            var violations = new List<string>();
            foreach (var file in _csharpFiles)
            {
                var fileName = Path.GetFileName(file);
                if (!fileName.EndsWith("Controller.cs")) continue;

                var content = File.ReadAllText(file);
                if (!content.Contains("Controller") && !content.Contains("ApiController"))
                    violations.Add($"Invalid controller inheritance in {fileName}");
            }
            return violations;
        }

        private List<string> GetAsyncViolations()
        {
            var violations = new List<string>();
            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                var fileName = Path.GetFileName(file);

                var asyncMethods = Regex.Matches(content, @"public\s+async\s+([^{]+)");
                foreach (Match match in asyncMethods)
                {
                    var methodSignature = match.Value;
                    if (!methodSignature.Contains("Task") && !methodSignature.Contains("void"))
                        violations.Add($"Invalid async method in {fileName}: {methodSignature}");
                }
            }
            return violations;
        }

        private List<string> GetUsingViolations()
        {
            var violations = new List<string>();
            foreach (var file in _csharpFiles)
            {
                var content = File.ReadAllText(file);
                var fileName = Path.GetFileName(file);

                if (content.Contains("using Microsoft.Extensions"))
                    violations.Add($"Incompatible using in {fileName}");
                if (content.Contains("using Microsoft.AspNetCore"))
                    violations.Add($"Incompatible using in {fileName}");
            }
            return violations;
        }

        private List<string> GetProjectViolations()
        {
            var violations = new List<string>();
            foreach (var projectFile in _projectFiles)
            {
                var content = File.ReadAllText(projectFile);
                var fileName = Path.GetFileName(projectFile);

                if (!content.Contains("TargetFrameworkVersion>v4.8</TargetFrameworkVersion>"))
                    violations.Add($"Wrong target framework in {fileName}");
                if (content.Contains("System.Text.Json"))
                    violations.Add($"Incompatible dependency in {fileName}");
                if (content.Contains("System.IO.Pipelines"))
                    violations.Add($"Incompatible dependency in {fileName}");
            }
            return violations;
        }

        private List<string> GetWebConfigViolations()
        {
            var violations = new List<string>();
            foreach (var configFile in _configFiles)
            {
                var content = File.ReadAllText(configFile);
                var fileName = Path.GetFileName(configFile);

                if (fileName == "Web.config")
                {
                    if (!content.Contains("targetFramework=\"4.8\""))
                        violations.Add($"Wrong target framework in {fileName}");
                    if (!content.Contains("<system.web>") && !content.Contains("<system.webServer>"))
                        violations.Add($"Missing required sections in {fileName}");
                }
            }
            return violations;
        }
    }
} 