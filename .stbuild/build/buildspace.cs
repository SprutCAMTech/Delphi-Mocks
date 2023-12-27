using System;
using System.IO;
using System.Collections.Generic;
using SprutCAMTech.BuildSystem.SettingsReader;
using SprutCAMTech.BuildSystem.SettingsReader.Object;
using SprutCAMTech.BuildSystem.Variants;
using SprutCAMTech.BuildSystem.ProjectList.Common;
using SprutCAMTech.BuildSystem.Package;
using SprutCAMTech.BuildSystem.Builder.MsDelphi;
using SprutCAMTech.BuildSystem.VersionManager.Common;
using SprutCAMTech.BuildSystem.ProjectCache.NuGet;
using SprutCAMTech.BuildSystem.HashGenerator.Common;
using SprutCAMTech.BuildSystem.HashGenerator;
using SprutCAMTech.BuildSystem.Restorer.Nuget;
using SprutCAMTech.BuildSystem.Cleaner.Common;
using SprutCAMTech.BuildSystem.PackageManager.Nuget;
using SprutCAMTech.BuildSystem.ProjectList.Helpers.BuildInfoSaver.Common;
using SprutCAMTech.BuildSystem.ProjectList.Helpers.Analyzer.Common;
using SprutCAMTech.BuildSystem.ProjectList.Helpers.SourceHashCalculator.Common;
using SprutCAMTech.BuildSystem.ProjectList.Helpers.Compiler.Common;
using SprutCAMTech.BuildSystem.ProjectList.Helpers.ProjectRestorer.Common;
using SprutCAMTech.BuildSystem.ProjectList.Helpers.CopierBuildResults.Common;
using SprutCAMTech.BuildSystem.ProjectList.Helpers.Deployer.Common;

/// <inheritdoc />
class BuildSpaceSettings : SettingsObject
{
    const StringComparison IGNCASE = StringComparison.CurrentCultureIgnoreCase;
    ReaderJson readerJson;

    /// <inheritdoc />
    /// <param name="configFiles"> Json configuration file paths </param>
    public BuildSpaceSettings(string[] configFiles) : base() {
        readerJson = new ReaderJson(Build.Logger);
        readerJson.ReadRules(configFiles);
        ReaderLocalVars = readerJson.LocalVars;
        ReaderDefines = readerJson.Defines;

        Projects = new HashSet<string>() {
            Path.Combine(Build.RootDirectory, "../DelphiMocksPackage/main/.stbuild/DelphiMocksProject.json"),
        };

        ProjectListProps = new ProjectListCommonProps(Build.Logger) {
            BuildInfoSaverProps = new BuildInfoSaverCommonProps(),
            AnalyzerProps = new AnalyzerCommonProps(),
            SourceHashCalculatorProps = new SourceHashCalculatorCommonProps(),
            CompilerProps = new CompilerCommonProps(),
            CopierBuildResultsProps = new CopierBuildResultsCommonProps(),
            DeployerProps = new DeployerCommonProps(),
            ProjectRestorerProps = new ProjectRestorerCommonProps
            {
                RestoreInsteadOfBuild = (info) => false
            },
            GetNextVersion = GetNextVersion.FromRemotePackages
        };

        RegisterBSObjects();
    }

    /// <summary>
    /// Register Build System control objects
    /// </summary>
    private void RegisterBSObjects() {
        Variants = new() {
            new() {
                Name = "Debug_x64",
                Configurations = new() { [Variant.NodeConfig] = "Debug" },
                Platforms =      new() { [Variant.NodePlatform] = "Win64" }
            },
            new() {
                Name = "Release_x64",
                Configurations = new() { [Variant.NodeConfig] = "Release" },
                Platforms =      new() { [Variant.NodePlatform] = "Win64" }
            },
            new() {
                Name = "Debug_x32",
                Configurations = new() { [Variant.NodeConfig] = "Debug" },
                Platforms =      new() { [Variant.NodePlatform] = "Win32" }
            },
            new() {
                Name = "Release_x32",
                Configurations = new() { [Variant.NodeConfig] = "Release" },
                Platforms =      new() { [Variant.NodePlatform] = "Win32" }
            }
        };

        AddManagerProp("builder_delphi", new() {"Release_x64", "Release_x32"}, builderDelphiRelease);
        AddManagerProp("builder_delphi",  new() {"Debug_x64", "Debug_x32"}, builderDelphiDebug);
        AddManagerProp("package_manager", null, packageManagerNuget);
        AddManagerProp("version_manager", null, versionManagerCommon);
        AddManagerProp("hash_generator", null, hashGeneratorCommon);
        AddManagerProp("restorer", null, restorerNuget);
        AddManagerProp("cleaner", null, cleanerCommon);
        AddManagerProp("cleaner_delphi", null, cleanerCommonDelphi);
        AddManagerProp("project_cache", null, projectCacheNuGet);
    }

    BuilderMsDelphiProps builderDelphiRelease => new() {
        Name = "builder_delphi_release",
        MsBuilderPath = readerJson.LocalVars["msbuilder_path"],
        EnvBdsPath = readerJson.LocalVars["env_bds"],
        RsVarsPath = readerJson.LocalVars["rsvars_path"],
        AutoClean = true,
        BuildParams = new Dictionary<string, string?>
        {
            ["-verbosity"] = "normal",
            ["-consoleloggerparameters"] = "ErrorsOnly",
            ["-nologo"] = "true",
            ["/p:DCC_Warnings"] = "false",
            ["/p:DCC_Hints"] = "false",
            ["/p:DCC_MapFile"] = "3",
            ["/p:DCC_AssertionsAtRuntime"] = "true",
            ["/p:DCC_DebugInformation"] = "2",
            ["/p:DCC_DebugDCUs"] = "false",
            ["/p:DCC_IOChecking"] = "true",
            ["/p:DCC_WriteableConstants"] = "true",
            ["/t:build"] = "true",
            ["/p:DCC_Optimize"] = "false",
            ["/p:DCC_GenerateStackFrames"] = "false",
            ["/p:DCC_LocalDebugSymbols"] = "false",
            ["/p:DCC_SymbolReferenceInfo"] = "0",
            ["/p:DCC_IntegerOverflowCheck"] = "false",
            ["/p:DCC_RangeChecking"] = "false"
        }
    };

    BuilderMsDelphiProps builderDelphiDebug {
        get {
            var bdelphi = new BuilderMsDelphiProps(builderDelphiRelease);
            bdelphi.Name = "builder_delphi_debug";
            bdelphi.BuildParams["/p:DCC_GenerateStackFrames"] = "true";
            bdelphi.BuildParams["/p:DCC_LocalDebugSymbols"] = "true";
            bdelphi.BuildParams["/p:DCC_SymbolReferenceInfo"] = "2";
            bdelphi.BuildParams["/p:DCC_IntegerOverflowCheck"] = "true";
            bdelphi.BuildParams["/p:DCC_RangeChecking"] = "true";
            return bdelphi;
        }
    }

    VersionManagerCommonProps versionManagerCommon {
        get {
            var branch = Build.GitBranch;
            var vmcp = new VersionManagerCommonProps();
            vmcp.Name = "version_manager_common";
            vmcp.DepthSearch = 2;
            vmcp.StartValue = 1;
            vmcp.UnstableVersionGap = 100;
            vmcp.PullRequestBranchPrefix = "c-";
            vmcp.DevelopBranchName = branch.EndsWith("develop", IGNCASE) ? branch : "develop";
            vmcp.MasterBranchName =  branch.EndsWith("master", IGNCASE) ? branch : "master";
            vmcp.ReleaseBranchName = branch.EndsWith("release", IGNCASE) ? branch : "release";
            return vmcp;
        }
    }

    ProjectCacheNuGetProps projectCacheNuGet => new() {
        Name = "project_cache_nuget",
        VersionManagerProps = versionManagerCommon,
        PackageManagerProps = packageManagerNuget,
        TempDir = "./hash"
    };

    HashGeneratorCommonProps hashGeneratorCommon => new() {
        Name = "hash_generator_main",
        HashAlgorithmType = HashAlgorithmType.Sha256
    };

    RestorerNugetProps restorerNuget => new() { 
        Name = "restorer_main" 
    };

    CleanerCommonProps cleanerCommon => new() {
        Name = "cleaner_default_main",
        AllBuildResults = true
    };

    CleanerCommonProps cleanerCommonDelphi => new() {
        Name = "cleaner_delphi_main",
        AllBuildResults = true,
        Paths = new Dictionary<string, List<string>>
        {
            ["$project:output_dcu$"] = new() { "*.dcu" }
        }
    };

    PackageManagerNugetProps packageManagerNuget => new() {
        Name = "package_manager_nuget_rc",
        SetStorageInfo = SetStorageInfoFunc
    };

    private StorageInfo SetStorageInfoFunc(PackageAction packageAction, string packageId, VersionProp? packageVersion) {
        var si = new StorageInfo() {
            Url = readerJson.LocalVars.GetValueOrDefault("nuget_source"),
            ApiKey = readerJson.LocalVars.GetValueOrDefault("nuget_api_key")
        };

        Build.Logger.debug($"SetStorageInfoFunc: url={si.Url} - apiKey has " + !string.IsNullOrEmpty(si.ApiKey));

        return si;
    }

}