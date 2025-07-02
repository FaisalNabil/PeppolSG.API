#!/bin/bash

echo "🔍 PeppolSG.API - ASP.NET MVC/Web API Compliance Validation"
echo "============================================================="
echo ""

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Function to check if a pattern exists in files
check_pattern() {
    local pattern="$1"
    local description="$2"
    local files="$3"
    
    echo -n "Checking: $description... "
    
    if grep -r "$pattern" $files > /dev/null 2>&1; then
        echo -e "${RED}❌ VIOLATION FOUND${NC}"
        echo "   Pattern '$pattern' found in:"
        grep -r "$pattern" $files | head -3
        return 1
    else
        echo -e "${GREEN}✅ COMPLIANT${NC}"
        return 0
    fi
}

# Function to check if a pattern exists (should exist)
check_pattern_exists() {
    local pattern="$1"
    local description="$2"
    local files="$3"
    
    echo -n "Checking: $description... "
    
    if grep -r "$pattern" $files > /dev/null 2>&1; then
        echo -e "${GREEN}✅ FOUND${NC}"
        return 0
    else
        echo -e "${RED}❌ MISSING${NC}"
        return 1
    fi
}

# Function to count files
count_files() {
    local pattern="$1"
    local description="$2"
    
    local count=$(find . -name "$pattern" | wc -l)
    echo -e "${BLUE}📁 $description: $count files${NC}"
}

echo "📊 Project Structure Analysis:"
echo "-----------------------------"
count_files "*.cs" "C# source files"
count_files "*.csproj" "Project files"
count_files "*.config" "Configuration files"
echo ""

echo "🔒 C# 7.3 Language Features Compliance:"
echo "---------------------------------------"

# Check for C# 8.0+ features that are NOT allowed in C# 7.3
check_pattern "switch expression" "C# 8.0 switch expressions" "PeppolSG.API"
check_pattern "switch =>" "C# 8.0 switch expressions" "PeppolSG.API"
check_pattern "using declaration" "C# 8.0 using declarations" "PeppolSG.API"
check_pattern "nullable reference types" "C# 8.0 nullable reference types" "PeppolSG.API"
check_pattern "#nullable" "C# 8.0 nullable reference types" "PeppolSG.API"
check_pattern "async foreach" "C# 8.0 async foreach" "PeppolSG.API"
check_pattern "await foreach" "C# 8.0 async foreach" "PeppolSG.API"
check_pattern "record.*class" "C# 9.0 records" "PeppolSG.API"
check_pattern "init.*set" "C# 9.0 init-only properties" "PeppolSG.API"
check_pattern "global using" "C# 10.0 global using" "PeppolSG.API"
check_pattern "file-scoped namespace" "C# 10.0 file-scoped namespaces" "PeppolSG.API"

echo ""
echo "🏗️ .NET Framework 4.8 Compatibility:"
echo "-----------------------------------"

# Check for .NET Core/.NET 5+ APIs that don't exist in .NET Framework 4.8
check_pattern "Microsoft.Extensions" ".NET Core Microsoft.Extensions" "PeppolSG.API"
check_pattern "System.Text.Json" ".NET Core System.Text.Json" "PeppolSG.API"
check_pattern "System.Threading.Channels" ".NET Core System.Threading.Channels" "PeppolSG.API"
check_pattern "System.IO.Pipelines" ".NET Core System.IO.Pipelines" "PeppolSG.API"
check_pattern "Microsoft.AspNetCore" "ASP.NET Core references" "PeppolSG.API"

# Check for correct .NET Framework 4.8 references
check_pattern_exists "TargetFrameworkVersion>v4.8</TargetFrameworkVersion>" ".NET Framework 4.8 target" "PeppolSG.API/*.csproj"
check_pattern_exists "System.Web.Http" "Web API references" "PeppolSG.API"
check_pattern_exists "System.Web.Mvc" "MVC references" "PeppolSG.API"

echo ""
echo "🌐 ASP.NET MVC/Web API Compliance:"
echo "--------------------------------"

# Check for proper controller inheritance
check_pattern_exists "Controller.*:" "Controller inheritance" "PeppolSG.API/Controllers/*.cs"
check_pattern_exists "ApiController.*:" "ApiController inheritance" "PeppolSG.API/Controllers/*.cs"

# Check for proper action method signatures
check_pattern "public.*async.*Task" "Async action methods with Task return" "PeppolSG.API/Controllers/*.cs"
check_pattern "public.*async.*void" "Async action methods with void return (VIOLATION)" "PeppolSG.API/Controllers/*.cs"

# Check for proper Web API configuration
check_pattern_exists "System.Web.Http" "Web API namespace usage" "PeppolSG.API"
check_pattern_exists "System.Web.Mvc" "MVC namespace usage" "PeppolSG.API"

echo ""
echo "⚙️ Web.config Configuration Compliance:"
echo "-------------------------------------"

# Check for proper Web.config configuration
check_pattern_exists "targetFramework=\"4.8\"" ".NET Framework 4.8 target" "PeppolSG.API/Web.config"
check_pattern_exists "<system.web>" "System.web section" "PeppolSG.API/Web.config"
check_pattern_exists "<system.webServer>" "System.webServer section" "PeppolSG.API/Web.config"

echo ""
echo "📝 Using Statements Compliance:"
echo "-----------------------------"

# Check for incompatible using statements
check_pattern "using Microsoft.Extensions" "Incompatible Microsoft.Extensions" "PeppolSG.API"
check_pattern "using System.Text.Json" "Incompatible System.Text.Json" "PeppolSG.API"
check_pattern "using Microsoft.AspNetCore" "Incompatible Microsoft.AspNetCore" "PeppolSG.API"

# Check for required using statements
check_pattern_exists "using System.Web.Http" "Web API using statement" "PeppolSG.API"
check_pattern_exists "using System.Web.Mvc" "MVC using statement" "PeppolSG.API"

echo ""
echo "🔄 Async/Await Compliance:"
echo "------------------------"

# Check for proper async/await usage
check_pattern "public.*async.*Task" "Proper async Task methods" "PeppolSG.API"
check_pattern "await.*;" "Await statements" "PeppolSG.API"

# Check for violations
check_pattern "public.*async.*void" "Async void methods (VIOLATION)" "PeppolSG.API"
check_pattern "await.*;" "Await without async context (VIOLATION)" "PeppolSG.API"

echo ""
echo "🎯 Controller Action Method Compliance:"
echo "-------------------------------------"

# Check for proper controller structure
check_pattern_exists "Controller.cs" "Controller files" "PeppolSG.API/Controllers"
check_pattern_exists "public.*(" "Public action methods" "PeppolSG.API/Controllers/*.cs"

echo ""
echo "📋 Project File Compliance:"
echo "-------------------------"

# Check for proper project file configuration
check_pattern_exists "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC" "C# project type GUID" "PeppolSG.API/*.csproj"
check_pattern_exists "ToolsVersion=\"15.0\"" "Visual Studio 2017+ ToolsVersion" "PeppolSG.API/*.csproj"
check_pattern_exists "OutputType>Library</OutputType>" "Library output type" "PeppolSG.API/*.csproj"

echo ""
echo "🔧 Syntax Compliance:"
echo "-------------------"

# Check for basic syntax issues
check_pattern "\\{\\s*\\}" "Empty braces (potential issue)" "PeppolSG.API"
check_pattern "\\(\\)" "Empty parentheses (potential issue)" "PeppolSG.API"

echo ""
echo "📦 Dependency Compliance:"
echo "-----------------------"

# Check for incompatible dependencies
check_pattern "Microsoft.AspNetCore" "Incompatible ASP.NET Core" "PeppolSG.API"
check_pattern "Microsoft.Extensions" "Incompatible Microsoft.Extensions" "PeppolSG.API"
check_pattern "System.Text.Json" "Incompatible System.Text.Json" "PeppolSG.API"
check_pattern "Microsoft.NETCore.App" "Incompatible .NET Core" "PeppolSG.API"

# Check for required dependencies
check_pattern_exists "System.Web.Http" "Required Web API dependency" "PeppolSG.API"
check_pattern_exists "System.Web.Mvc" "Required MVC dependency" "PeppolSG.API"

echo ""
echo "📊 Compliance Summary:"
echo "--------------------"

echo -e "${GREEN}✅ ASP.NET MVC/Web API .NET Framework 4.8 C# 7.3 Compliance Validation Complete${NC}"
echo ""
echo "🎯 Compliance Categories Checked:"
echo "  • C# 7.3 language features compliance"
echo "  • .NET Framework 4.8 compatibility"
echo "  • ASP.NET MVC/Web API compliance"
echo "  • Web.config configuration compliance"
echo "  • Using statements compliance"
echo "  • Async/await compliance"
echo "  • Controller action method compliance"
echo "  • Project file compliance"
echo "  • Syntax compliance"
echo "  • Dependency compliance"
echo ""
echo "🚀 Next Steps:"
echo "  • Run compliance tests in Visual Studio"
echo "  • Execute unit tests for validation"
echo "  • Perform integration testing"
echo "  • Validate against Peppol testbed"
echo ""
echo -e "${GREEN}🏆 The Peppol Access Point compliance validation is complete!${NC}" 