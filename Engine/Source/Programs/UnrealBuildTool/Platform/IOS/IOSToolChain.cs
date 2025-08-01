// Copyright Epic Games, Inc. All Rights Reserved.


using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Diagnostics;
using System.Security.AccessControl;
using System.Xml;
using System.Xml.Linq;
using System.Text;
using Ionic.Zip;
using Ionic.Zlib;
using Tools.DotNETCommon;
using System.Security.Cryptography.X509Certificates;

namespace UnrealBuildTool
{
	class IOSToolChainSettings : AppleToolChainSettings
	{
		/// <summary>
		/// The version of the iOS SDK to target at build time.
		/// </summary>
		[XmlConfigFile(Category = "IOSToolChain")]
		public string IOSSDKVersion = "latest";
		public readonly float IOSSDKVersionFloat = 0.0f;

		/// <summary>
		/// The version of the iOS to allow at build time.
		/// </summary>
		[XmlConfigFile(Category = "IOSToolChain")]
		public string BuildIOSVersion = "7.0";

		/// <summary>
		/// Directory for the developer binaries
		/// </summary>
		public string ToolchainDir = "";

		/// <summary>
		/// Location of the SDKs
		/// </summary>
		public readonly string BaseSDKDir;
		public readonly string BaseSDKDirSim;

		public readonly string DevicePlatformName;
		public readonly string SimulatorPlatformName;

		public IOSToolChainSettings() : this("iPhoneOS", "iPhoneSimulator")
		{
		}

		protected IOSToolChainSettings(string DevicePlatformName, string SimulatorPlatformName) : base(true)
		{
			XmlConfig.ApplyTo(this);

			this.DevicePlatformName = DevicePlatformName;
			this.SimulatorPlatformName = SimulatorPlatformName;

			// update cached paths
			BaseSDKDir = XcodeDeveloperDir + "Platforms/" + DevicePlatformName + ".platform/Developer/SDKs";
			BaseSDKDirSim = XcodeDeveloperDir + "Platforms/" + SimulatorPlatformName + ".platform/Developer/SDKs";
			ToolchainDir = XcodeDeveloperDir + "Toolchains/XcodeDefault.xctoolchain/usr/bin/";

			// make sure SDK is selected
			SelectSDK(BaseSDKDir, DevicePlatformName, ref IOSSDKVersion, true);

			// convert to float for easy comparison
			IOSSDKVersionFloat = float.Parse(IOSSDKVersion, System.Globalization.CultureInfo.InvariantCulture);
		}

		public string GetSDKPath(string Architecture)
		{
			if (Architecture == "-simulator")
			{
				return BaseSDKDirSim + "/" + SimulatorPlatformName + IOSSDKVersion + ".sdk";
			}

			return BaseSDKDir + "/" + DevicePlatformName + IOSSDKVersion + ".sdk";
		}
	}

	class IOSToolChain : AppleToolChain
	{
		private static List<FileItem> BundleDependencies = new List<FileItem>();

		public readonly ReadOnlyTargetRules Target;
		protected IOSProjectSettings ProjectSettings;

		public IOSToolChain(ReadOnlyTargetRules Target, IOSProjectSettings InProjectSettings)
			: this(Target, InProjectSettings, () => new IOSToolChainSettings())
		{
		}

		protected IOSToolChain(ReadOnlyTargetRules Target, IOSProjectSettings InProjectSettings, Func<IOSToolChainSettings> InCreateSettings)
			: base((Target == null) ? null : Target.ProjectFile)
		{
			this.Target = Target;
			ProjectSettings = InProjectSettings;
			Settings = new Lazy<IOSToolChainSettings>(InCreateSettings);
		}

		// ***********************************************************************
		// * NOTE:
		// *  Do NOT change the defaults to set your values, instead you should set the environment variables
		// *  properly in your system, as other tools make use of them to work properly!
		// *  The defaults are there simply for examples so you know what to put in your env vars...
		// ***********************************************************************

		// If you are looking for where to change the remote compile server name, look in RemoteToolChain.cs

		/// <summary>
		/// If this is set, then we do not do any post-compile steps -- except moving the executable into the proper spot on Mac.
		/// </summary>
		[XmlConfigFile]
		public static bool bUseDangerouslyFastMode = false;

		/// <summary>
		/// The lazily constructed settings for the toolchain
		/// </summary>
		private Lazy<IOSToolChainSettings> Settings;

		/// <summary>
		/// Which compiler frontend to use
		/// </summary>
		private const string IOSCompiler = "clang++";

		/// <summary>
		/// Which linker frontend to use
		/// </summary>
		private const string IOSLinker = "clang++";

		/// <summary>
		/// Which library archiver to use
		/// </summary>
		private const string IOSArchiver = "libtool";

		public override string GetSDKVersion()
		{
			return Settings.Value.IOSSDKVersionFloat.ToString();
		}

		public override void ModifyBuildProducts(ReadOnlyTargetRules Target, UEBuildBinary Binary, List<string> Libraries, List<UEBuildBundleResource> BundleResources, Dictionary<FileReference, BuildProductType> BuildProducts)
		{
			if (Target.IOSPlatform.bCreateStubIPA && Binary.Type != UEBuildBinaryType.StaticLibrary)
			{
				FileReference StubFile = FileReference.Combine(Binary.OutputFilePath.Directory, Binary.OutputFilePath.GetFileNameWithoutExtension() + ".stub");
				BuildProducts.Add(StubFile, BuildProductType.Package);

				if (Target.Platform == UnrealTargetPlatform.TVOS)
				{
					FileReference AssetFile = FileReference.Combine(Binary.OutputFilePath.Directory, "AssetCatalog", "Assets.car");
					BuildProducts.Add(AssetFile, BuildProductType.RequiredResource);
				}
				else if (Target.Platform == UnrealTargetPlatform.IOS && Settings.Value.IOSSDKVersionFloat >= 11.0f)
				{
					int Index = Binary.OutputFilePath.GetFileNameWithoutExtension().IndexOf("-");
					string OutputFile = Binary.OutputFilePath.GetFileNameWithoutExtension().Substring(0, Index > 0 ? Index : Binary.OutputFilePath.GetFileNameWithoutExtension().Length);
					FileReference AssetFile = FileReference.Combine(Binary.OutputFilePath.Directory, "Payload", OutputFile + ".app", "Assets.car");
					BuildProducts.Add(AssetFile, BuildProductType.RequiredResource);
					AssetFile = FileReference.Combine(Binary.OutputFilePath.Directory, "Payload", OutputFile + ".app", "AppIcon60x60@2x.png");
					BuildProducts.Add(AssetFile, BuildProductType.RequiredResource);
					AssetFile = FileReference.Combine(Binary.OutputFilePath.Directory, "Payload", OutputFile + ".app", "AppIcon76x76@2x~ipad.png");
					BuildProducts.Add(AssetFile, BuildProductType.RequiredResource);
				}
			}
			if (Target.IOSPlatform.bGeneratedSYM && (ProjectSettings.bGenerateCrashReportSymbols || Target.bUseMallocProfiler) && Binary.Type == UEBuildBinaryType.StaticLibrary)
			{
				FileReference DebugFile = FileReference.Combine(Binary.OutputFilePath.Directory, Binary.OutputFilePath.GetFileNameWithoutExtension() + ".udebugsymbols");
				BuildProducts.Add(DebugFile, BuildProductType.SymbolFile);
			}
		}

		/// <summary>
		/// Adds a build product and its associated debug file to a receipt.
		/// </summary>
		/// <param name="OutputFile">Build product to add</param>
		/// <param name="OutputType">Type of build product</param>
		public override bool ShouldAddDebugFileToReceipt(FileReference OutputFile, BuildProductType OutputType)
		{
			return OutputType == BuildProductType.Executable || OutputType == BuildProductType.DynamicLibrary;
		}

		public override FileReference GetDebugFile(FileReference OutputFile, string DebugExtension)
		{
			if (OutputFile.FullName.Contains(".framework"))
			{
				// need to put the debug info outside of the framework
				return FileReference.Combine(OutputFile.Directory.ParentDirectory, OutputFile.ChangeExtension(DebugExtension).GetFileName());
			}
			//  by default, just change the extension to the debug extension
			return OutputFile.ChangeExtension(DebugExtension);
		}


		string GetCompileArguments_Global(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";

			Result += " -fmessage-length=0";
			Result += " -pipe";
			Result += " -fpascal-strings";

			if (!Utils.IsRunningOnMono)
			{
				Result += " -fdiagnostics-format=msvc";
			}

			// Optionally enable exception handling (off by default since it generates extra code needed to propagate exceptions)
			if (CompileEnvironment.bEnableExceptions)
			{
				Result += " -fexceptions";
			}
			else
			{
				Result += " -fno-exceptions";
			}

			string SanitizerMode = Environment.GetEnvironmentVariable("ENABLE_ADDRESS_SANITIZER");
			if (SanitizerMode != null && SanitizerMode == "YES")
			{
				Result += " -fsanitize=address";
			}

			string UndefSanitizerMode = Environment.GetEnvironmentVariable("ENABLE_UNDEFINED_BEHAVIOR_SANITIZER");
			if (UndefSanitizerMode != null && UndefSanitizerMode == "YES")
			{
				Result += " -fsanitize=undefined -fno-sanitize=bounds,enum,return,float-divide-by-zero";
			}

			Result += GetRTTIFlag(CompileEnvironment);
			Result += " -fvisibility=hidden"; // hides the linker warnings with PhysX

			// use LTO if desired (like VCToolchain does)
			if (CompileEnvironment.bAllowLTCG)
			{
				Result += " -flto";
			}

			Result += " -Wall -Werror";
			Result += " -Wdelete-non-virtual-dtor";

			// There's a number of places where boolean logic uses "&" and "|" instead of "&&" and "||"
			// Remove the warning for now
			if (GetClangVersion() >= new Version(14, 0, 3))
			{
				Result += " -Wno-bitwise-instead-of-logical ";
			}

			// clang 12.00 has a new warning for copies in ranged loops. Instances have all been fixed up (2020/6/26) but
			// are likely to be reintroduced due to no equivalent on other platforms at this time so disable the warning
			// See also MacToolChain.cs
			if (GetClangVersion().Major >= 12)
			{
				Result += " -Wno-range-loop-analysis";

				// We have 'this' vs nullptr comparisons that get optimized away for newer versions of Clang
				Result += " -fno-delete-null-pointer-checks";
			}

			// Xcode 13.3 / Apple Clang 13.1.6 new flags
			// Apple Clang binary releases are proprietary closed source different from open source clang versions
			// See also ClangToolChain.cs in UE5
			if (GetClangVersion() >= new Version(13, 1, 6))
			{
				Result += " -Wno-unused-but-set-variable";
				Result += " -Wno-unused-but-set-parameter";
				Result += " -Wno-ordered-compare-function-pointers";
			}
			
			// Starting with Xcode16.3 (clang 17), clang is detecting some uses of cxx-extensions,
			// such as: "error: variable length arrays in C++ are a Clang extension" 
			// Remove this warning on Apple platforms, for now.
			if (GetClangVersion() >= new Version(17, 0, 0))
			{
				Result += " -Wno-vla-cxx-extension";
			}

			if (CompileEnvironment.ShadowVariableWarningLevel != WarningLevel.Off)
			{
				// Xcode 16 has better shadow variable detection that triggers errors all over the engine.
				Result += " -Wshadow" + ((CompileEnvironment.ShadowVariableWarningLevel == WarningLevel.Error && GetClangVersion() < new Version(16, 0, 0)) ? "" : " -Wno-error=shadow");
			}

			if (CompileEnvironment.bEnableUndefinedIdentifierWarnings)
			{
				Result += " -Wundef" + (CompileEnvironment.bUndefinedIdentifierWarningsAsErrors ? "" : " -Wno-error=undef");
			}

			// fix for Xcode 8.3 enabling nonportable include checks, but p4 has some invalid cases in it
			if (Settings.Value.IOSSDKVersionFloat >= 10.3)
			{
				Result += " -Wno-nonportable-include-path";
			}

			if (IsBitcodeCompilingEnabled(CompileEnvironment.Configuration))
			{
				Result += " -fembed-bitcode";
			}

			Result += " -c";

			// What architecture(s) to build for
			Result += GetArchitectureArgument(CompileEnvironment.Configuration, CompileEnvironment.Architecture);

			Result += string.Format(" -isysroot \"{0}\"", Settings.Value.GetSDKPath(CompileEnvironment.Architecture));

			Result += " -m" + GetXcodeMinVersionParam() + "=" + ProjectSettings.RuntimeVersion;

			bool bStaticAnalysis = false;
			string StaticAnalysisMode = Environment.GetEnvironmentVariable("CLANG_STATIC_ANALYZER_MODE");
			if (StaticAnalysisMode != null && StaticAnalysisMode != "")
			{
				bStaticAnalysis = true;
			}

			// Optimize non- debug builds.
			if (CompileEnvironment.bOptimizeCode && !bStaticAnalysis)
			{
				if (CompileEnvironment.bOptimizeForSize)
				{
					Result += " -Oz";
				}
				else
				{
					Result += " -O3";
				}
			}
			else
			{
				Result += " -O0";
			}

			if (!CompileEnvironment.bUseInlining)
			{
				Result += " -fno-inline-functions";
			}

			// Create DWARF format debug info if wanted,
			if (CompileEnvironment.bCreateDebugInfo)
			{
				Result += " -gdwarf-2";
			}

			// Add additional frameworks so that their headers can be found
			foreach (UEBuildFramework Framework in CompileEnvironment.AdditionalFrameworks)
			{
				if (Framework.FrameworkDirectory != null)
				{
					string FrameworkDir = Framework.FrameworkDirectory.FullName;
					// embedded frameworks have a framework inside of this directory, so we use this directory. regular frameworks need to go one up to point to the 
					// directory containing the framework. -F gives a path to look for the -framework
					if (FrameworkDir.EndsWith(".framework"))
					{
						FrameworkDir = Path.GetDirectoryName(FrameworkDir);
					}
					Result += String.Format(" -F\"{0}\"", FrameworkDir);
				}
			}

			return Result;
		}

		static string GetCppStandardCompileArgument(CppCompileEnvironment CompileEnvironment)
		{
			var Mapping = new Dictionary<CppStandardVersion, string>
			{
				{ CppStandardVersion.Cpp14, " -std=c++14" },
				{ CppStandardVersion.Cpp17, " -std=c++17" },
				{ CppStandardVersion.Latest, " -std=c++17" },
				{ CppStandardVersion.Default, " -std=c++14" }
			};
			return Mapping[CompileEnvironment.CppStandard];
		}

		static string GetCompileArguments_CPP(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";
			Result += " -x objective-c++";
			Result += GetCppStandardCompileArgument(CompileEnvironment);
			Result += " -stdlib=libc++";
			return Result;
		}

		static string GetCompileArguments_MM(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";
			Result += " -x objective-c++";
			Result += GetCppStandardCompileArgument(CompileEnvironment);
			Result += " -stdlib=libc++";
			return Result;
		}

		static string GetCompileArguments_M(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";
			Result += " -x objective-c";
			Result += " -stdlib=libc++";
			return Result;
		}

		static string GetCompileArguments_C()
		{
			string Result = "";
			Result += " -x c";
			return Result;
		}

		static string GetCompileArguments_PCH(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";
			Result += " -x objective-c++-header";
			Result += GetCppStandardCompileArgument(CompileEnvironment);
			Result += " -stdlib=libc++";
			return Result;
		}

		// Conditionally enable (default disabled) generation of information about every class with virtual functions for use by the C++ runtime type identification features 
		// (`dynamic_cast' and `typeid'). If you don't use those parts of the language, you can save some space by using -fno-rtti. 
		// Note that exception handling uses the same information, but it will generate it as needed. 
		static string GetRTTIFlag(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";

			if (CompileEnvironment.bUseRTTI)
			{
				Result = " -frtti";
			}
			else
			{
				Result = " -fno-rtti";
			}

			return Result;
		}

		// Conditionally enable (default disabled) Objective-C exceptions
		static string GetObjCExceptionsFlag(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";

			if (CompileEnvironment.bEnableObjCExceptions)
			{
				Result += " -fobjc-exceptions";
			}
			else
			{
				Result += " -fno-objc-exceptions";
			}

			return Result;
		}

		static string GetObjCEnableAutomaticReferenceCounting(CppCompileEnvironment CompileEnvironment)
		{
			string Result = "";

			if (CompileEnvironment.bEnableObjCAutomaticReferenceCounting)
			{
				Result += " -fobjc-arc";
			}

			return Result;
		}

		static void CleanIntermediateDirectory(string Path)
		{
			string ResultsText;

			// Delete the local dest directory if it exists
			if (Directory.Exists(Path))
			{
				// this can deal with linked files
				RunExecutableAndWait("rm", String.Format("-rf \"{0}\"", Path), out ResultsText);
			}

			// Create the intermediate local directory
			RunExecutableAndWait("mkdir", String.Format("-p \"{0}\"", Path), out ResultsText);
		}

		bool IsBitcodeCompilingEnabled(CppConfiguration Configuration)
		{
			return Configuration == CppConfiguration.Shipping && ProjectSettings.bShipForBitcode;
		}

		public virtual string GetXcodeMinVersionParam()
		{
			return "iphoneos-version-min";
		}

		public virtual string GetArchitectureArgument(CppConfiguration Configuration, string UBTArchitecture)
		{
			// get the list of architectures to compile
			string Archs =
				UBTArchitecture == "-simulator" ? "i386" :
				String.Join(",", (Configuration == CppConfiguration.Shipping) ? ProjectSettings.ShippingArchitectures : ProjectSettings.NonShippingArchitectures);

			Log.TraceLogOnce("Compiling with these architectures: " + Archs);

			// parse the string
			string[] Tokens = Archs.Split(",".ToCharArray());


			string Result = "";

			foreach (string Token in Tokens)
			{
				Result += " -arch " + Token;
			}

			//  Remove this in 4.16 
			//  Commented this out, for now. @Pete let's conditionally check this when we re-implement this fix. 
			//  Result += " -mcpu=cortex-a9";

			return Result;
		}

		public string GetAdditionalLinkerFlags(CppConfiguration InConfiguration)
		{
			if (InConfiguration != CppConfiguration.Shipping)
			{
				return ProjectSettings.AdditionalLinkerFlags;
			}
			else
			{
				return ProjectSettings.AdditionalShippingLinkerFlags;
			}
		}

		string GetLinkArguments_Global(LinkEnvironment LinkEnvironment)
		{
			string Result = "";

			Result += GetArchitectureArgument(LinkEnvironment.Configuration, LinkEnvironment.Architecture);

			bool bIsDevice = (LinkEnvironment.Architecture != "-simulator");
			Result += String.Format(" -isysroot \\\"{0}Platforms/{1}.platform/Developer/SDKs/{1}{2}.sdk\\\"",
				Settings.Value.XcodeDeveloperDir, bIsDevice ? Settings.Value.DevicePlatformName : Settings.Value.SimulatorPlatformName, Settings.Value.IOSSDKVersion);

			if (IsBitcodeCompilingEnabled(LinkEnvironment.Configuration))
			{
				FileItem OutputFile = FileItem.GetItemByFileReference(LinkEnvironment.OutputFilePath);

				Result += " -fembed-bitcode -Xlinker -bitcode_verify -Xlinker -bitcode_hide_symbols -Xlinker -bitcode_symbol_map ";
				Result += " -Xlinker \\\"" + Path.GetDirectoryName(OutputFile.AbsolutePath) + "\\\"";
			}

			Result += " -dead_strip";
			Result += " -m" + GetXcodeMinVersionParam() + "=" + ProjectSettings.RuntimeVersion;
			Result += " -Wl";
			if (!IsBitcodeCompilingEnabled(LinkEnvironment.Configuration))
			{
				Result += "-no_pie";
			}
			Result += " -stdlib=libc++";
			Result += " -ObjC";
			//			Result += " -v";

			// use LTO if desired (like VCToolchain does)
			if (LinkEnvironment.bAllowLTCG)
			{
				Result += " -flto";
			}

			// Apple's new linker in Xcode 15 has issues with some templated classes and dynamic linking and will 
			// print to the log dup library warnings (amongst others), such as: "ld: warning: ignoring duplicate libraries: '-lc++'"
			if (GetClangVersion() >= new Version(15, 0, 0))
			{
				Result += " -ld_classic";
			}

			string SanitizerMode = Environment.GetEnvironmentVariable("ENABLE_ADDRESS_SANITIZER");
			if (SanitizerMode != null && SanitizerMode == "YES")
			{
				Result += " -rpath \"@executable_path/Frameworks\"";
				Result += " -fsanitize=address";
			}

			string UndefSanitizerMode = Environment.GetEnvironmentVariable("ENABLE_UNDEFINED_BEHAVIOR_SANITIZER");
			if (UndefSanitizerMode != null && UndefSanitizerMode == "YES")
			{
				Result += " -rpath \"@executable_path/libclang_rt.ubsan_ios_dynamic.dylib\"";
				Result += " -fsanitize=undefined";
			}

			// need to tell where to load Framework dylibs
			Result += " -rpath @executable_path/Frameworks";

			Result += " " + GetAdditionalLinkerFlags(LinkEnvironment.Configuration);

			// link in the frameworks
			foreach (string Framework in LinkEnvironment.Frameworks)
			{
				if (Framework != "ARKit" || Settings.Value.IOSSDKVersionFloat >= 11.0f)
				{
					Result += " -framework " + Framework;
				}
			}
			foreach (UEBuildFramework Framework in LinkEnvironment.AdditionalFrameworks)
			{
				if (Framework.FrameworkDirectory != null)
				{
					// If this framework has a directory specified, we'll need to setup the path as well
					string FrameworkDir = Framework.FrameworkDirectory.FullName;

					// embedded frameworks have a framework inside of this directory, so we use this directory. regular frameworks need to go one up to point to the 
					// directory containing the framework. -F gives a path to look for the -framework
					if (FrameworkDir.EndsWith(".framework"))
					{
						FrameworkDir = Path.GetDirectoryName(FrameworkDir);
					}
					Result += String.Format(" -F\\\"{0}\\\"", FrameworkDir);
				}

				Result += " -framework " + Framework.Name;
			}
			foreach (string Framework in LinkEnvironment.WeakFrameworks)
			{
				Result += " -weak_framework " + Framework;
			}

			return Result;
		}

		static string GetArchiveArguments_Global(LinkEnvironment LinkEnvironment)
		{
			string Result = "";

			Result += " -static";

			return Result;
		}

		public override CPPOutput CompileCPPFiles(CppCompileEnvironment CompileEnvironment, List<FileItem> InputFiles, DirectoryReference OutputDir, string ModuleName, IActionGraphBuilder Graph)
		{
			string Arguments = GetCompileArguments_Global(CompileEnvironment);
			string PCHArguments = "";

			if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Include)
			{
				PCHArguments += string.Format(" -include \"{0}\"", CompileEnvironment.PrecompiledHeaderIncludeFilename.FullName);
			}

			foreach (FileItem ForceIncludeFile in CompileEnvironment.ForceIncludeFiles)
			{
				Arguments += String.Format(" -include \"{0}\"", ForceIncludeFile.Location.FullName);
			}

			// Add include paths to the argument list.
			HashSet<DirectoryReference> AllIncludes = new HashSet<DirectoryReference>(CompileEnvironment.UserIncludePaths);
			AllIncludes.UnionWith(CompileEnvironment.SystemIncludePaths);
			foreach (DirectoryReference IncludePath in AllIncludes)
			{
				Arguments += string.Format(" -I\"{0}\"", IncludePath.FullName);
			}

			foreach (string Definition in CompileEnvironment.Definitions)
			{
				Arguments += string.Format(" -D\"{0}\"", Definition);
			}

			CPPOutput Result = new CPPOutput();
			// Create a compile action for each source file.
			foreach (FileItem SourceFile in InputFiles)
			{
				Action CompileAction = Graph.CreateAction(ActionType.Compile);
				string FilePCHArguments = "";
				string FileArguments = "";
				string Extension = Path.GetExtension(SourceFile.AbsolutePath).ToUpperInvariant();

				if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Create)
				{
					// Compile the file as a C++ PCH.
					FileArguments += GetCompileArguments_PCH(CompileEnvironment);
					FileArguments += GetRTTIFlag(CompileEnvironment);
					FileArguments += GetObjCExceptionsFlag(CompileEnvironment);
					FileArguments += GetObjCEnableAutomaticReferenceCounting(CompileEnvironment);
				}
				else if (Extension == ".C")
				{
					// Compile the file as C code.
					FileArguments += GetCompileArguments_C();
				}
				else if (Extension == ".MM")
				{
					// Compile the file as Objective-C++ code.
					FileArguments += GetCompileArguments_MM(CompileEnvironment);
					FileArguments += GetRTTIFlag(CompileEnvironment);
					FileArguments += GetObjCExceptionsFlag(CompileEnvironment);
					FileArguments += GetObjCEnableAutomaticReferenceCounting(CompileEnvironment);
				}
				else if (Extension == ".M")
				{
					// Compile the file as Objective-C code.
					FileArguments += GetCompileArguments_M(CompileEnvironment);
					FileArguments += GetObjCExceptionsFlag(CompileEnvironment);
					FileArguments += GetObjCEnableAutomaticReferenceCounting(CompileEnvironment);
				}
				else
				{
					// Compile the file as C++ code.
					FileArguments += GetCompileArguments_CPP(CompileEnvironment);
					FileArguments += GetRTTIFlag(CompileEnvironment);
					FileArguments += GetObjCExceptionsFlag(CompileEnvironment);
					FileArguments += GetObjCEnableAutomaticReferenceCounting(CompileEnvironment);

					// only use PCH for .cpp files
					FilePCHArguments = PCHArguments;
				}

				// Add the C++ source file and its included files to the prerequisite item list.
				CompileAction.PrerequisiteItems.Add(SourceFile);

				// Add the precompiled header
				if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Include)
				{
					CompileAction.PrerequisiteItems.Add(CompileEnvironment.PrecompiledHeaderFile);
				}

				// Upload the force included files
				foreach (FileItem ForceIncludeFile in CompileEnvironment.ForceIncludeFiles)
				{
					CompileAction.PrerequisiteItems.Add(ForceIncludeFile);
				}

				string OutputFilePath = null;
				if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Create)
				{
					// Add the precompiled header file to the produced item list.
					FileItem PrecompiledHeaderFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, Path.GetFileName(SourceFile.AbsolutePath) + ".gch"));

					CompileAction.ProducedItems.Add(PrecompiledHeaderFile);
					Result.PrecompiledHeaderFile = PrecompiledHeaderFile;

					// Add the parameters needed to compile the precompiled header file to the command-line.
					FileArguments += string.Format(" -o \"{0}\"", PrecompiledHeaderFile.AbsolutePath);
				}
				else
				{
					if (CompileEnvironment.PrecompiledHeaderAction == PrecompiledHeaderAction.Include)
					{
						CompileAction.PrerequisiteItems.Add(CompileEnvironment.PrecompiledHeaderFile);
					}

					string ObjectFileExtension;
					if (CompileEnvironment.AdditionalArguments != null && CompileEnvironment.AdditionalArguments.Contains("-emit-llvm"))
					{
						ObjectFileExtension = ".bc";
					}
					else
					{
						ObjectFileExtension = ".o";
					}

					// Add the object file to the produced item list.
					FileItem ObjectFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, Path.GetFileName(SourceFile.AbsolutePath) + ObjectFileExtension));

					CompileAction.ProducedItems.Add(ObjectFile);
					Result.ObjectFiles.Add(ObjectFile);
					FileArguments += string.Format(" -o \"{0}\"", ObjectFile.AbsolutePath);
					OutputFilePath = ObjectFile.AbsolutePath;
				}

				// Add the source file path to the command-line.
				FileArguments += string.Format(" \"{0}\"", SourceFile.AbsolutePath);

				// Generate the included header dependency list
				if (CompileEnvironment.bGenerateDependenciesFile)
				{
					FileItem DependencyListFile = FileItem.GetItemByFileReference(FileReference.Combine(OutputDir, Path.GetFileName(SourceFile.AbsolutePath) + ".d"));
					FileArguments += string.Format(" -MD -MF\"{0}\"", DependencyListFile.AbsolutePath.Replace('\\', '/'));
					CompileAction.DependencyListFile = DependencyListFile;
					CompileAction.ProducedItems.Add(DependencyListFile);
				}

				string CompilerPath = Settings.Value.ToolchainDir + IOSCompiler;

				string AllArgs = FilePCHArguments + Arguments + FileArguments + CompileEnvironment.AdditionalArguments;
				/*				string SourceText = System.IO.File.ReadAllText(SourceFile.AbsolutePath);
								if (CompileEnvironment.bOptimizeForSize && (SourceFile.AbsolutePath.Contains("ElementBatcher.cpp") || SourceText.Contains("ElementBatcher.cpp") || SourceFile.AbsolutePath.Contains("AnimationRuntime.cpp") || SourceText.Contains("AnimationRuntime.cpp")
									|| SourceFile.AbsolutePath.Contains("AnimEncoding.cpp") || SourceText.Contains("AnimEncoding.cpp") || SourceFile.AbsolutePath.Contains("TextRenderComponent.cpp") || SourceText.Contains("TextRenderComponent.cpp")
									|| SourceFile.AbsolutePath.Contains("SWidget.cpp") || SourceText.Contains("SWidget.cpp") || SourceFile.AbsolutePath.Contains("SCanvas.cpp") || SourceText.Contains("SCanvas.cpp") || SourceFile.AbsolutePath.Contains("ShaderCore.cpp") || SourceText.Contains("ShaderCore.cpp")
									|| SourceFile.AbsolutePath.Contains("ParticleSystemRender.cpp") || SourceText.Contains("ParticleSystemRender.cpp")))
								{
									Log.TraceInformation("Forcing {0} to --O3!", SourceFile.AbsolutePath);

									AllArgs = AllArgs.Replace("-Oz", "-O3");
								}*/

				// Analyze and then compile using the shell to perform the indirection
				string StaticAnalysisMode = Environment.GetEnvironmentVariable("CLANG_STATIC_ANALYZER_MODE");
				if (StaticAnalysisMode != null && StaticAnalysisMode != "" && OutputFilePath != null)
				{
					string TempArgs = "-c \"" + CompilerPath + " " + AllArgs + " --analyze -Wno-unused-command-line-argument -Xclang -analyzer-output=html -Xclang -analyzer-config -Xclang path-diagnostics-alternate=true -Xclang -analyzer-config -Xclang report-in-main-source-file=true -Xclang -analyzer-disable-checker -Xclang deadcode.DeadStores -o " + OutputFilePath.Replace(".o", ".html") + "; " + CompilerPath + " " + AllArgs + "\"";
					AllArgs = TempArgs;
					CompilerPath = "/bin/sh";
				}

				// RPC utility parameters are in terms of the Mac side
				CompileAction.WorkingDirectory = GetMacDevSrcRoot();
				CompileAction.CommandPath = new FileReference(CompilerPath);
				CompileAction.CommandArguments = AllArgs; // Arguments + FileArguments + CompileEnvironment.AdditionalArguments;
				CompileAction.CommandDescription = "Compile";
				CompileAction.StatusDescription = string.Format("{0}", Path.GetFileName(SourceFile.AbsolutePath));
				CompileAction.bIsGCCCompiler = true;
				// We're already distributing the command by execution on Mac.
				CompileAction.bCanExecuteRemotely = true;
				CompileAction.bShouldOutputStatusDescription = true;
				CompileAction.CommandVersion = GetFullClangVersion();

				foreach (UEBuildFramework Framework in CompileEnvironment.AdditionalFrameworks)
				{
					if (Framework.ZipFile != null)
					{
						FileItem ExtractedTokenFile = ExtractFramework(Framework, Graph);
						CompileAction.PrerequisiteItems.Add(ExtractedTokenFile);
					}
				}
			}
			return Result;
		}

		public override FileItem LinkFiles(LinkEnvironment LinkEnvironment, bool bBuildImportLibraryOnly, IActionGraphBuilder Graph)
		{
			string LinkerPath = Settings.Value.ToolchainDir +
				(LinkEnvironment.bIsBuildingLibrary ? IOSArchiver : IOSLinker);

			// Create an action that invokes the linker.
			Action LinkAction = Graph.CreateAction(ActionType.Link);

			// RPC utility parameters are in terms of the Mac side
			LinkAction.WorkingDirectory = GetMacDevSrcRoot();

			// build this up over the rest of the function
			string LinkCommandArguments = LinkEnvironment.bIsBuildingLibrary ? GetArchiveArguments_Global(LinkEnvironment) : GetLinkArguments_Global(LinkEnvironment);
			if (LinkEnvironment.bIsBuildingDLL)
			{
				// @todo roll this put into GetLinkArguments_Global
				LinkerPath = IOSLinker;
				LinkCommandArguments += " -dynamiclib -Xlinker -export_dynamic -Xlinker -no_deduplicate";

				string InstallName = LinkEnvironment.InstallName ?? String.Format("@executable_path/Frameworks/{0}", LinkEnvironment.OutputFilePath.MakeRelativeTo(LinkEnvironment.OutputFilePath.Directory.ParentDirectory));
				LinkCommandArguments += string.Format(" -Xlinker -install_name -Xlinker {0}", InstallName);

				LinkCommandArguments += " -Xlinker -rpath -Xlinker @executable_path/Frameworks";
				LinkCommandArguments += " -Xlinker -rpath -Xlinker @loader_path/Frameworks";
			}

			if (!LinkEnvironment.bIsBuildingLibrary)
			{
				// Add the library paths to the argument list.
				foreach (DirectoryReference LibraryPath in LinkEnvironment.SystemLibraryPaths)
				{
					LinkCommandArguments += string.Format(" -L\\\"{0}\\\"", LibraryPath.FullName);
				}

				// Add the additional libraries to the argument list.
				foreach (string AdditionalLibrary in LinkEnvironment.SystemLibraries)
				{
					LinkCommandArguments += string.Format(" -l\\\"{0}\\\"", AdditionalLibrary);
				}

				foreach(FileReference Library in LinkEnvironment.Libraries)
				{
					// for absolute library paths, convert to remote filename
					// add it to the prerequisites to make sure it's built first (this should be the case of non-system libraries)
					FileItem LibFile = FileItem.GetItemByFileReference(Library);
					LinkAction.PrerequisiteItems.Add(LibFile);

					// and add to the commandline
					LinkCommandArguments += string.Format(" \\\"{0}\\\"", Library.FullName);
				}
			}

			// Handle additional framework assets that might need to be shadowed
			foreach (UEBuildFramework Framework in LinkEnvironment.AdditionalFrameworks)
			{
				if (Framework.ZipFile != null)
				{
					FileItem ExtractedTokenFile = ExtractFramework(Framework, Graph);
					LinkAction.PrerequisiteItems.Add(ExtractedTokenFile);
				}
			}

			// Add the output file as a production of the link action.
			FileItem OutputFile = FileItem.GetItemByFileReference(LinkEnvironment.OutputFilePath);
			LinkAction.ProducedItems.Add(OutputFile);

			// Add arguments to generate a map file too
			if ((!LinkEnvironment.bIsBuildingLibrary || LinkEnvironment.bIsBuildingDLL) && LinkEnvironment.bCreateMapFile)
			{
				FileItem MapFile = FileItem.GetItemByFileReference(new FileReference(OutputFile.Location.FullName + ".map"));
				LinkCommandArguments += string.Format(" -Wl,-map,\\\"{0}\\\"", MapFile.Location.FullName);
				LinkAction.ProducedItems.Add(MapFile);
			}

			// Add the input files to a response file, and pass the response file on the command-line.
			List<string> InputFileNames = new List<string>();
			foreach (FileItem InputFile in LinkEnvironment.InputFiles)
			{
				string FileNamePath = string.Format("\"{0}\"", InputFile.AbsolutePath);
				InputFileNames.Add(FileNamePath);
				LinkAction.PrerequisiteItems.Add(InputFile);
			}

			// Write the list of input files to a response file, with a tempfilename, on remote machine
			if (LinkEnvironment.bIsBuildingLibrary)
			{
				foreach (string Filename in InputFileNames)
				{
					LinkCommandArguments += " " + Filename;
				}
				// @todo rocket lib: the -filelist command should take a response file (see else condition), except that it just says it can't
				// find the file that's in there. Rocket.lib may overflow the commandline by putting all files on the commandline, so this 
				// may be needed:
				// LinkCommandArguments += string.Format(" -filelist \"{0}\"", ConvertPath(ResponsePath));
			}
			else
			{
				bool bIsUE4Game = LinkEnvironment.OutputFilePath.FullName.Contains("UE4Game");
				FileReference ResponsePath = FileReference.Combine(((!bIsUE4Game && ProjectFile != null) ? ProjectFile.Directory : UnrealBuildTool.EngineDirectory), "Intermediate", "Build", LinkEnvironment.Platform.ToString(), "LinkFileList_" + LinkEnvironment.OutputFilePath.GetFileNameWithoutExtension() + ".tmp");
				Graph.CreateIntermediateTextFile(ResponsePath, InputFileNames);
				LinkCommandArguments += string.Format(" \\\"@{0}\\\"", ResponsePath.FullName);
			}

			// if we are making an LTO build, write the lto file next to build so dsymutil can find it
			if (LinkEnvironment.bAllowLTCG)
			{
				string LtoObjectFile;
				if (Target.bShouldCompileAsDLL)
				{
					// go up a directory so we don't put this big file into the framework
					LtoObjectFile = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(OutputFile.AbsolutePath)), Path.GetFileName(OutputFile.AbsolutePath) + ".lto.o");
				}
				else
				{
					LtoObjectFile = OutputFile.AbsolutePath + ".lto.o";
				}
				LinkCommandArguments += string.Format(" -flto -Xlinker -object_path_lto -Xlinker \\\"{0}\\\"", LtoObjectFile);
			}

			// Add the output file to the command-line.
			LinkCommandArguments += string.Format(" -o \\\"{0}\\\"", OutputFile.AbsolutePath);

			// Add the additional arguments specified by the environment.
			LinkCommandArguments += LinkEnvironment.AdditionalArguments;

			// Only execute linking on the local PC.
			LinkAction.bCanExecuteRemotely = false;

			LinkAction.StatusDescription = string.Format("{0}", OutputFile.AbsolutePath);

			LinkAction.CommandVersion = GetFullClangVersion();

			LinkAction.CommandPath = BuildHostPlatform.Current.Shell;
			if (LinkEnvironment.Configuration == CppConfiguration.Shipping && Path.GetExtension(OutputFile.AbsolutePath) != ".a")
			{
				// When building a shipping package, symbols are stripped from the exe as the last build step. This is a problem
				// when re-packaging and no source files change because the linker skips symbol generation and dsymutil will 
				// recreate a new .dsym file from a symboless exe file. It's just sad. To make things happy we need to delete 
				// the output file to force the linker to recreate it with symbols again.
				string linkCommandArguments = "-c \"";

				linkCommandArguments += string.Format("rm -f \\\"{0}\\\";", OutputFile.AbsolutePath);
				linkCommandArguments += string.Format("rm -f \\\"{0}\\*.bcsymbolmap\\\";", Path.GetDirectoryName(OutputFile.AbsolutePath));
				linkCommandArguments += "\\\"" + LinkerPath + "\\\" " + LinkCommandArguments + ";";

				linkCommandArguments += "\"";

				LinkAction.CommandArguments = linkCommandArguments;
			}
			else
			{
				// This is not a shipping build so no need to delete the output file since symbols will not have been stripped from it.
				LinkAction.CommandArguments = string.Format("-c \"\\\"{0}\\\" {1}\"", LinkerPath, LinkCommandArguments);
			}

			return OutputFile;
		}

		static string GetAppBundleName(FileReference Executable)
		{
			// Get the app bundle name
			string AppBundleName = Executable.GetFileNameWithoutExtension();

			// Strip off any platform suffix
			int SuffixIdx = AppBundleName.IndexOf('-');
			if (SuffixIdx != -1)
			{
				AppBundleName = AppBundleName.Substring(0, SuffixIdx);
			}

			// Append the .app suffix
			return AppBundleName + ".app";
		}

		public static FileReference GetAssetCatalogFile(UnrealTargetPlatform Platform, FileReference Executable)
		{
			// Get the output file
			if (Platform == UnrealTargetPlatform.IOS)
			{
				return FileReference.Combine(Executable.Directory, "Payload", GetAppBundleName(Executable), "Assets.car");
			}
			else
			{
				return FileReference.Combine(Executable.Directory, "AssetCatalog", "Assets.car");
			}
		}

		public static string GetAssetCatalogArgs(UnrealTargetPlatform Platform, string InputDir, string OutputDir)
		{
			StringBuilder Arguments = new StringBuilder("actool");
			Arguments.Append(" --output-format human-readable-text");
			Arguments.Append(" --notices");
			Arguments.Append(" --warnings");
			Arguments.AppendFormat(" --output-partial-info-plist \"{0}/assetcatalog_generated_info.plist\"", InputDir);
			if (Platform == UnrealTargetPlatform.TVOS)
			{
				Arguments.Append(" --app-icon \"App Icon & Top Shelf Image\"");
				Arguments.Append(" --launch-image \"Launch Image\"");
				Arguments.Append(" --filter-for-device-model AppleTV5,3");
				Arguments.Append(" --target-device tv");
				Arguments.Append(" --minimum-deployment-target 12.0");
				Arguments.Append(" --platform appletvos");
			}
			else
			{
				Arguments.Append(" --app-icon AppIcon");
				Arguments.Append(" --product-type com.apple.product-type.application");
				Arguments.Append(" --target-device iphone");
				Arguments.Append(" --target-device ipad");
				Arguments.Append(" --minimum-deployment-target 12.0");
				Arguments.Append(" --platform iphoneos");
			}
			Arguments.Append(" --enable-on-demand-resources YES");
			Arguments.AppendFormat(" --compile \"{0}\"", OutputDir);
			Arguments.AppendFormat(" \"{0}/Assets.xcassets\"", InputDir);
			return Arguments.ToString();
		}

		private static bool IsCompiledAsFramework(string ExecutablePath)
		{
			// @todo ios: Get the receipt to here which has the property
			return ExecutablePath.Contains(".framework");
		}

		private static string GetdSYMPath(FileItem Executable)
		{
			string ExecutablePath = Executable.AbsolutePath;
			// for frameworks, we want to put the .dSYM outside of the framework, and the executable is inside the .framework
			if (IsCompiledAsFramework(ExecutablePath))
			{
				return Path.Combine(Path.GetDirectoryName(ExecutablePath), "..", Path.GetFileName(ExecutablePath) + ".dSYM");
			}

			// return standard dSYM location
			return Path.Combine(Path.GetDirectoryName(ExecutablePath), Path.GetFileName(ExecutablePath) + ".dSYM");
		}

		/// <summary>
		/// Generates debug info for a given executable
		/// </summary>
		/// <param name="Executable">FileItem describing the executable to generate debug info for</param>
		/// <param name="bIsForLTOBuild">Was this build made with LTO enabled?</param>
		/// <param name="Graph">List of actions to be executed. Additional actions will be added to this list.</param>
		private List<FileItem> GenerateDebugInfo(FileItem Executable, bool bIsForLTOBuild, IActionGraphBuilder Graph)
		{
			// Make a file item for the source and destination files
			string FullDestPathRoot = GetdSYMPath(Executable);

			FileItem OutputFile = FileItem.GetItemByPath(FullDestPathRoot);
			FileItem ZipOutputFile = FileItem.GetItemByPath(FullDestPathRoot + ".zip");

			// Make the compile action
			Action GenDebugAction = Graph.CreateAction(ActionType.GenerateDebugInfo);

			GenDebugAction.WorkingDirectory = GetMacDevSrcRoot();
			GenDebugAction.CommandPath = BuildHostPlatform.Current.Shell;
			string ExtraOptions;
			string DsymutilPath = GetDsymutilPath(out ExtraOptions, bIsForLTOBuild: bIsForLTOBuild);
			if (ProjectSettings.bGeneratedSYMBundle)
			{
				GenDebugAction.CommandArguments = string.Format("-c \"rm -rf \\\"{2}\\\"; \\\"{0}\\\" \\\"{1}\\\" {4} -o \\\"{2}\\\"; cd \\\"{2}/..\\\"; zip -r -y -1 {3}.zip {3}\"",
					DsymutilPath,
					Executable.AbsolutePath,
					OutputFile.AbsolutePath,
					Path.GetFileName(FullDestPathRoot),
					ExtraOptions);
				GenDebugAction.ProducedItems.Add(ZipOutputFile);
				Log.TraceInformation("Zip file: " + ZipOutputFile.AbsolutePath);
			}
			else
			{
				GenDebugAction.CommandArguments = string.Format("-c \"rm -rf \\\"{2}\\\"; \\\"{0}\\\" \\\"{1}\\\" {3} -f -o \\\"{2}\\\"\"",
						DsymutilPath,
						Executable.AbsolutePath,
						OutputFile.AbsolutePath,
						ExtraOptions);
			}
			GenDebugAction.PrerequisiteItems.Add(Executable);
			GenDebugAction.ProducedItems.Add(OutputFile);
			GenDebugAction.StatusDescription = GenDebugAction.CommandArguments;// string.Format("Generating debug info for {0}", Path.GetFileName(Executable.AbsolutePath));
			GenDebugAction.bCanExecuteRemotely = false;

			return GenDebugAction.ProducedItems; // (ProjectSettings.bGeneratedSYMBundle ? ZipOutputFile : OutputFile);
		}

		/// <summary>
		/// Generates pseudo pdb info for a given executable
		/// </summary>
		/// <param name="Executable">FileItem describing the executable to generate debug info for</param>
		/// <param name="Graph">List of actions to be executed. Additional actions will be added to this list.</param>
		public FileItem GeneratePseudoPDB(FileItem Executable, IActionGraphBuilder Graph)
		{
			// Make a file item for the source and destination files
			string FulldSYMPathRoot = GetdSYMPath(Executable);
			string FullDestPathRoot = Path.ChangeExtension(FulldSYMPathRoot, ".udebugsymbols");
			string PathToDWARF = Path.Combine(FulldSYMPathRoot, "Contents", "Resources", "DWARF", Path.GetFileName(Executable.AbsolutePath));

			FileItem dSYMFile = FileItem.GetItemByPath(FulldSYMPathRoot);

			FileItem DWARFFile = FileItem.GetItemByPath(PathToDWARF);

			FileItem OutputFile = FileItem.GetItemByPath(FullDestPathRoot);

			// Make the compile action
			Action GenDebugAction = Graph.CreateAction(ActionType.GenerateDebugInfo);
			GenDebugAction.WorkingDirectory = DirectoryReference.Combine(UnrealBuildTool.EngineDirectory, "Binaries", "Mac");

			GenDebugAction.CommandPath = BuildHostPlatform.Current.Shell;
			GenDebugAction.CommandArguments = string.Format("-c \"rm -rf \\\"{1}\\\"; dwarfdump --uuid \\\"{3}\\\" | cut -d\\  -f2; chmod 777 ./DsymExporter; ./DsymExporter -UUID=$(dwarfdump --uuid \\\"{3}\\\" | cut -d\\  -f2) \\\"{0}\\\" \\\"{2}\\\"\"",
					DWARFFile.AbsolutePath,
					OutputFile.AbsolutePath,
					Path.GetDirectoryName(OutputFile.AbsolutePath),
					dSYMFile.AbsolutePath);
			GenDebugAction.PrerequisiteItems.Add(dSYMFile);
			GenDebugAction.ProducedItems.Add(OutputFile);
			GenDebugAction.StatusDescription = GenDebugAction.CommandArguments;// string.Format("Generating debug info for {0}", Path.GetFileName(Executable.AbsolutePath));
			GenDebugAction.bCanExecuteRemotely = false;

			return OutputFile;
		}

		private static void PackageStub(string BinaryPath, string GameName, string ExeName)
		{
			// create the ipa
			string IPAName = BinaryPath + "/" + ExeName + ".stub";
			// delete the old one
			if (File.Exists(IPAName))
			{
				File.Delete(IPAName);
			}

			// make the subdirectory if needed
			string DestSubdir = Path.GetDirectoryName(IPAName);
			if (!Directory.Exists(DestSubdir))
			{
				Directory.CreateDirectory(DestSubdir);
			}

			// set up the directories
			string ZipWorkingDir = String.Format("Payload/{0}.app/", GameName);
			string ZipSourceDir = string.Format("{0}/Payload/{1}.app", BinaryPath, GameName);

			// create the file
			using (ZipFile Zip = new ZipFile())
			{
				// add the entire directory
				Zip.AddDirectory(ZipSourceDir, ZipWorkingDir);

				// Update permissions to be UNIX-style
				// Modify the file attributes of any added file to unix format
				foreach (ZipEntry E in Zip.Entries)
				{
					const byte FileAttributePlatform_NTFS = 0x0A;
					const byte FileAttributePlatform_UNIX = 0x03;
					const byte FileAttributePlatform_FAT = 0x00;

					const int UNIX_FILETYPE_NORMAL_FILE = 0x8000;
					//const int UNIX_FILETYPE_SOCKET = 0xC000;
					//const int UNIX_FILETYPE_SYMLINK = 0xA000;
					//const int UNIX_FILETYPE_BLOCKSPECIAL = 0x6000;
					const int UNIX_FILETYPE_DIRECTORY = 0x4000;
					//const int UNIX_FILETYPE_CHARSPECIAL = 0x2000;
					//const int UNIX_FILETYPE_FIFO = 0x1000;

					const int UNIX_EXEC = 1;
					const int UNIX_WRITE = 2;
					const int UNIX_READ = 4;


					int MyPermissions = UNIX_READ | UNIX_WRITE;
					int OtherPermissions = UNIX_READ;

					int PlatformEncodedBy = (E.VersionMadeBy >> 8) & 0xFF;
					int LowerBits = 0;

					// Try to preserve read-only if it was set
					bool bIsDirectory = E.IsDirectory;

					// Check to see if this 
					bool bIsExecutable = false;
					if (Path.GetFileNameWithoutExtension(E.FileName).Equals(GameName, StringComparison.InvariantCultureIgnoreCase))
					{
						bIsExecutable = true;
					}

					if (bIsExecutable)
					{
						// The executable will be encrypted in the final distribution IPA and will compress very poorly, so keeping it
						// uncompressed gives a better indicator of IPA size for our distro builds
						E.CompressionLevel = CompressionLevel.None;
					}

					if ((PlatformEncodedBy == FileAttributePlatform_NTFS) || (PlatformEncodedBy == FileAttributePlatform_FAT))
					{
						FileAttributes OldAttributes = E.Attributes;
						//LowerBits = ((int)E.Attributes) & 0xFFFF;

						if ((OldAttributes & FileAttributes.Directory) != 0)
						{
							bIsDirectory = true;
						}

						// Permissions
						if ((OldAttributes & FileAttributes.ReadOnly) != 0)
						{
							MyPermissions &= ~UNIX_WRITE;
							OtherPermissions &= ~UNIX_WRITE;
						}
					}

					if (bIsDirectory || bIsExecutable)
					{
						MyPermissions |= UNIX_EXEC;
						OtherPermissions |= UNIX_EXEC;
					}

					// Re-jigger the external file attributes to UNIX style if they're not already that way
					if (PlatformEncodedBy != FileAttributePlatform_UNIX)
					{
						int NewAttributes = bIsDirectory ? UNIX_FILETYPE_DIRECTORY : UNIX_FILETYPE_NORMAL_FILE;

						NewAttributes |= (MyPermissions << 6);
						NewAttributes |= (OtherPermissions << 3);
						NewAttributes |= (OtherPermissions << 0);

						// Now modify the properties
						E.AdjustExternalFileAttributes(FileAttributePlatform_UNIX, (NewAttributes << 16) | LowerBits);
					}
				}

				// Save it out
				Zip.Save(IPAName);
			}
		}

		FileItem ExtractFramework(UEBuildFramework Framework, IActionGraphBuilder Graph)
		{
			if (Framework.ZipFile == null)
			{
				throw new BuildException("Unable to extract framework '{0}' - no zip file specified", Framework.Name);
			}
			if(Framework.ExtractedTokenFile == null)
			{
				FileItem InputFile = FileItem.GetItemByFileReference(Framework.ZipFile);
				Framework.ExtractedTokenFile = FileItem.GetItemByFileReference(new FileReference(Framework.FrameworkDirectory.FullName + ".extracted"));

				StringBuilder ExtractScript = new StringBuilder();
				ExtractScript.AppendLine("#!/bin/sh");
				ExtractScript.AppendLine("set -e");
				// ExtractScript.AppendLine("set -x"); // For debugging
				ExtractScript.AppendLine(String.Format("[ -d {0} ] && rm -rf {0}", Utils.MakePathSafeToUseWithCommandLine(Framework.FrameworkDirectory.FullName)));
				ExtractScript.AppendLine(String.Format("unzip -q -o {0} -d {1}", Utils.MakePathSafeToUseWithCommandLine(Framework.ZipFile.FullName), Utils.MakePathSafeToUseWithCommandLine(Framework.FrameworkDirectory.ParentDirectory.FullName))); // Zip contains folder with the same name, hence ParentDirectory
				ExtractScript.AppendLine(String.Format("touch {0}", Utils.MakePathSafeToUseWithCommandLine(Framework.ExtractedTokenFile.AbsolutePath)));

				FileItem ExtractScriptFileItem = Graph.CreateIntermediateTextFile(new FileReference(Framework.FrameworkDirectory.FullName + ".sh"), ExtractScript.ToString());

				Action UnzipAction = Graph.CreateAction(ActionType.BuildProject);
				UnzipAction.CommandPath = new FileReference("/bin/sh");
				UnzipAction.CommandArguments = Utils.MakePathSafeToUseWithCommandLine(ExtractScriptFileItem.AbsolutePath);
				UnzipAction.WorkingDirectory = UnrealBuildTool.EngineDirectory;
				UnzipAction.PrerequisiteItems.Add(InputFile);
				UnzipAction.PrerequisiteItems.Add(ExtractScriptFileItem);
				UnzipAction.ProducedItems.Add(Framework.ExtractedTokenFile);
				UnzipAction.DeleteItems.Add(Framework.ExtractedTokenFile);
				UnzipAction.StatusDescription = String.Format("Unzipping : {0} -> {1}", Framework.ZipFile, Framework.FrameworkDirectory);
				UnzipAction.bCanExecuteRemotely = false;
			}
			return Framework.ExtractedTokenFile;
		}

		public static DirectoryReference GenerateAssetCatalog(FileReference ProjectFile, UnrealTargetPlatform Platform, ref bool bUserImagesExist)
		{
			string EngineDir = UnrealBuildTool.EngineDirectory.ToString();
			string BuildDir = (((ProjectFile != null) ? ProjectFile.Directory.ToString() : (string.IsNullOrEmpty(UnrealBuildTool.GetRemoteIniPath()) ? UnrealBuildTool.EngineDirectory.ToString() : UnrealBuildTool.GetRemoteIniPath()))) + "/Build/" + (Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS");
			string IntermediateDir = (((ProjectFile != null) ? ProjectFile.Directory.ToString() : UnrealBuildTool.EngineDirectory.ToString())) + "/Intermediate/" + (Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS");

			bUserImagesExist = false;

			string ResourcesDir = Path.Combine(IntermediateDir, "Resources");
			if (Platform == UnrealTargetPlatform.TVOS)
			{
				// copy the template asset catalog to the appropriate directory
				string Dir = Path.Combine(ResourcesDir, "Assets.xcassets");
				if (!Directory.Exists(Dir))
				{
					Directory.CreateDirectory(Dir);
				}
				// create the directories
				foreach (string directory in Directory.EnumerateDirectories(Path.Combine(EngineDir, "Build", "TVOS", "Resources", "Assets.xcassets"), "*", SearchOption.AllDirectories))
				{
					Dir = directory.Replace(Path.Combine(EngineDir, "Build", "TVOS"), IntermediateDir);
					if (!Directory.Exists(Dir))
					{
						Directory.CreateDirectory(Dir);
					}
				}
				// copy the default files
				foreach (string file in Directory.EnumerateFiles(Path.Combine(EngineDir, "Build", "TVOS", "Resources", "Assets.xcassets"), "*", SearchOption.AllDirectories))
				{
					Dir = file.Replace(Path.Combine(EngineDir, "Build", "TVOS"), IntermediateDir);
					File.Copy(file, Dir, true);
					FileInfo DestFileInfo = new FileInfo(Dir);
					DestFileInfo.Attributes = DestFileInfo.Attributes & ~FileAttributes.ReadOnly;
				}
				// copy the icons from the game directory if it has any
				string[][] Images = {
					new string []{ "Icon_Large_Back-1280x768.png", "App Icon & Top Shelf Image.brandassets/App Icon - Large.imagestack/Back.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Large_Front-1280x768.png", "App Icon & Top Shelf Image.brandassets/App Icon - Large.imagestack/Front.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Large_Middle-1280x768.png", "App Icon & Top Shelf Image.brandassets/App Icon - Large.imagestack/Middle.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Small_Back-400x240.png", "App Icon & Top Shelf Image.brandassets/App Icon.imagestack/Back.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Small_Back-800x480.png", "App Icon & Top Shelf Image.brandassets/App Icon.imagestack/Back.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Small_Front-400x240.png", "App Icon & Top Shelf Image.brandassets/App Icon.imagestack/Front.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Small_Front-800x480.png", "App Icon & Top Shelf Image.brandassets/App Icon.imagestack/Front.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Small_Middle-400x240.png", "App Icon & Top Shelf Image.brandassets/App Icon.imagestack/Middle.imagestacklayer/Content.imageset" },
					new string []{ "Icon_Small_Middle-800x480.png", "App Icon & Top Shelf Image.brandassets/App Icon.imagestack/Middle.imagestacklayer/Content.imageset" },
					new string []{ "TopShelfWide-1920x720@2x.png", "App Icon & Top Shelf Image.brandassets/Top Shelf Image Wide.imageset" },
					new string []{ "TopShelfWide-1920x720.png", "App Icon & Top Shelf Image.brandassets/Top Shelf Image Wide.imageset" },
					new string []{ "TopShelf.png", "App Icon & Top Shelf Image.brandassets/Top Shelf Image.imageset" },
					new string []{ "TopShelf@2x.png", "App Icon & Top Shelf Image.brandassets/Top Shelf Image.imageset" },
				};
				Dir = Path.Combine(IntermediateDir, "Resources", "Assets.xcassets");

				string BuildResourcesGraphicsDir = Path.Combine(BuildDir, "Resources", "Assets.xcassets");
				for (int Index = 0; Index < Images.Length; ++Index)
				{
					string SourceDir = Path.Combine((Directory.Exists(BuildResourcesGraphicsDir) ? (BuildDir) : (Path.Combine(EngineDir, "Build", "TVOS"))),
						"Resources",
						"Assets.xcassets");
					string Image = Path.Combine(SourceDir, Images[Index][1], Images[Index][0]);

					if (File.Exists(Image))
					{
						bUserImagesExist |= Image.StartsWith(BuildResourcesGraphicsDir);

						File.Copy(Image, Path.Combine(Dir, Images[Index][1], Images[Index][0]), true);
						FileInfo DestFileInfo = new FileInfo(Path.Combine(Dir, Images[Index][1], Images[Index][0]));
						DestFileInfo.Attributes = DestFileInfo.Attributes & ~FileAttributes.ReadOnly;
					}
				}
			}
			else
			{
				// copy the template asset catalog to the appropriate directory
				string Dir = Path.Combine(ResourcesDir, "Assets.xcassets");
				if (!Directory.Exists(Dir))
				{
					Directory.CreateDirectory(Dir);
				}
				// create the directories
				foreach (string directory in Directory.EnumerateDirectories(Path.Combine(EngineDir, "Build", "IOS", "Resources", "Assets.xcassets"), "*", SearchOption.AllDirectories))
				{
					Dir = directory.Replace(Path.Combine(EngineDir, "Build", "IOS"), IntermediateDir);
					if (!Directory.Exists(Dir))
					{
						Directory.CreateDirectory(Dir);
					}
				}
				// copy the default files
				foreach (string file in Directory.EnumerateFiles(Path.Combine(EngineDir, "Build", "IOS", "Resources", "Assets.xcassets"), "*", SearchOption.AllDirectories))
				{
					Dir = file.Replace(Path.Combine(EngineDir, "Build", "IOS"), IntermediateDir);
					File.Copy(file, Dir, true);
					FileInfo DestFileInfo = new FileInfo(Dir);
					DestFileInfo.Attributes = DestFileInfo.Attributes & ~FileAttributes.ReadOnly;
				}
				// copy the icons from the game directory if it has any
				string[][] Images = {
					new string []{ "IPhoneIcon20@2x.png", "Icon20@2x.png", "Icon40.png" },
					new string []{ "IPhoneIcon20@3x.png", "Icon20@3x.png", "Icon60.png" },
					new string []{ "IPhoneIcon29@2x.png", "Icon29@2x.png", "Icon58.png" },
					new string []{ "IPhoneIcon29@3x.png", "Icon29@3x.png", "Icon87.png" },
					new string []{ "IPhoneIcon40@2x.png", "Icon40@2x.png", "Icon80.png" },
					new string []{ "IPhoneIcon40@3x.png", "Icon40@3x.png", "Icon60@2x.png", "Icon120.png" },
					new string []{ "IPhoneIcon60@2x.png", "Icon60@2x.png", "Icon40@3x.png", "Icon120.png" },
					new string []{ "IPhoneIcon60@3x.png", "Icon60@3x.png", "Icon180.png" },
					new string []{ "IPadIcon20.png", "Icon20.png" },
					new string []{ "IPadIcon20@2x.png", "Icon20@2x.png", "Icon40.png" },
					new string []{ "IPadIcon29.png", "Icon29.png" },
					new string []{ "IPadIcon29@2x.png", "Icon29@2x.png", "Icon58.png" },
					new string []{ "IPadIcon40.png", "Icon40.png", "Icon20@2x.png" },
					new string []{ "IPadIcon40@2x.png", "Icon80.png", "Icon40@2x.png" },
					new string []{ "IPadIcon76.png", "Icon76.png" },
					new string []{ "IPadIcon76@2x.png", "Icon76@2x.png", "Icon152.png" },
					new string []{ "IPadIcon83.5@2x.png", "Icon83.5@2x.png", "Icon167.png" },
					new string []{ "Icon1024.png", "Icon1024.png" },
				};
				Dir = Path.Combine(IntermediateDir, "Resources", "Assets.xcassets", "AppIcon.appiconset");

				string BuildResourcesGraphicsDir = Path.Combine(BuildDir, "Resources", "Graphics");
				for (int Index = 0; Index < Images.Length; ++Index)
				{
					string Image = Path.Combine((Directory.Exists(Path.Combine(BuildDir, "Resources", "Graphics")) ? (BuildDir) : (Path.Combine(EngineDir, "Build", "IOS"))), "Resources", "Graphics", Images[Index][1]);
					if (!File.Exists(Image) && Images[Index].Length > 2)
					{
						Image = Path.Combine((Directory.Exists(Path.Combine(BuildDir, "Resources", "Graphics")) ? (BuildDir) : (Path.Combine(EngineDir, "Build", "IOS"))), "Resources", "Graphics", Images[Index][2]);
					}
					if (!File.Exists(Image) && Images[Index].Length > 3)
					{
						Image = Path.Combine((Directory.Exists(Path.Combine(BuildDir, "Resources", "Graphics")) ? (BuildDir) : (Path.Combine(EngineDir, "Build", "IOS"))), "Resources", "Graphics", Images[Index][3]);
					}
					if (File.Exists(Image))
					{
						bUserImagesExist |= Image.StartsWith(BuildResourcesGraphicsDir);

						File.Copy(Image, Path.Combine(Dir, Images[Index][0]), true);
						FileInfo DestFileInfo = new FileInfo(Path.Combine(Dir, Images[Index][0]));
						DestFileInfo.Attributes = DestFileInfo.Attributes & ~FileAttributes.ReadOnly;
					}
				}
			}
			return new DirectoryReference(ResourcesDir);
		}

		public override ICollection<FileItem> PostBuild(FileItem Executable, LinkEnvironment BinaryLinkEnvironment, IActionGraphBuilder Graph)
		{
			List<FileItem> OutputFiles = new List<FileItem>(base.PostBuild(Executable, BinaryLinkEnvironment, Graph));

			if (BinaryLinkEnvironment.bIsBuildingLibrary)
			{
				return OutputFiles;
			}

			// For IOS/tvOS, generate the dSYM file if needed or requested
			if (Target.IOSPlatform.bGeneratedSYM)
			{
				List<FileItem> Files = GenerateDebugInfo(Executable, BinaryLinkEnvironment.bAllowLTCG, Graph);
				foreach (FileItem item in Files)
				{
					OutputFiles.Add(item);
				}
				if (ProjectSettings.bGenerateCrashReportSymbols || Target.bUseMallocProfiler)
				{
					OutputFiles.Add(GeneratePseudoPDB(Executable, Graph));
				}
			}

			// strip the debug info from the executable if needed. creates a dummy output file for the action graph to track that it's out of date.
			if (Target.IOSPlatform.bStripSymbols || (Target.Configuration == UnrealTargetConfiguration.Shipping))
			{
				FileItem StripCompleteFile = FileItem.GetItemByFileReference(FileReference.Combine(BinaryLinkEnvironment.IntermediateDirectory, Executable.Location.GetFileName() + ".stripped"));

				// If building a framework we can only strip local symbols, need to leave global in place
				string StripArguments = BinaryLinkEnvironment.bIsBuildingDLL ? "-x" : "";

				Action StripAction = Graph.CreateAction(ActionType.CreateAppBundle);
				StripAction.WorkingDirectory = GetMacDevSrcRoot();
				StripAction.CommandPath = BuildHostPlatform.Current.Shell;
				StripAction.CommandArguments = String.Format("-c \"\\\"{0}strip\\\" {1} \\\"{2}\\\" && touch \\\"{3}\\\"\"", Settings.Value.ToolchainDir, StripArguments, Executable.Location, StripCompleteFile);
				StripAction.PrerequisiteItems.Add(Executable);
				StripAction.PrerequisiteItems.AddRange(OutputFiles);
				StripAction.ProducedItems.Add(StripCompleteFile);
				StripAction.StatusDescription = String.Format("Stripping symbols from {0}", Executable.AbsolutePath);
				StripAction.bCanExecuteRemotely = false;

				OutputFiles.Add(StripCompleteFile);
			}

			if (!BinaryLinkEnvironment.bIsBuildingDLL && ShouldCompileAssetCatalog())
			{
				// generate the asset catalog
				bool bUserImagesExist = false;
				DirectoryReference ResourcesDir = GenerateAssetCatalog(ProjectFile, BinaryLinkEnvironment.Platform, ref bUserImagesExist);

				// Get the output location for the asset catalog
				FileItem AssetCatalogFile = FileItem.GetItemByFileReference(GetAssetCatalogFile(BinaryLinkEnvironment.Platform, Executable.Location));

				// Make the compile action
				Action CompileAssetAction = Graph.CreateAction(ActionType.CreateAppBundle);
				CompileAssetAction.WorkingDirectory = GetMacDevSrcRoot();
				CompileAssetAction.CommandPath = new FileReference("/usr/bin/xcrun");
				CompileAssetAction.CommandArguments = GetAssetCatalogArgs(BinaryLinkEnvironment.Platform, ResourcesDir.FullName, Path.GetDirectoryName(AssetCatalogFile.AbsolutePath));
				CompileAssetAction.PrerequisiteItems.Add(Executable);
				CompileAssetAction.ProducedItems.Add(AssetCatalogFile);
				CompileAssetAction.DeleteItems.Add(AssetCatalogFile);
				CompileAssetAction.StatusDescription = CompileAssetAction.CommandArguments;// string.Format("Generating debug info for {0}", Path.GetFileName(Executable.AbsolutePath));
				CompileAssetAction.bCanExecuteRemotely = false;

				// Add it to the output files so it's always built
				OutputFiles.Add(AssetCatalogFile);
			}

			// Generate the app bundle
			if (!Target.bDisableLinking)
			{
				Log.TraceInformation("Adding PostBuildSync action");

				List<string> UPLScripts = UEDeployIOS.CollectPluginDataPaths(BinaryLinkEnvironment.AdditionalProperties);
				VersionNumber SdkVersion = VersionNumber.Parse(Settings.Value.IOSSDKVersion);

				Dictionary<string, DirectoryReference> FrameworkNameToSourceDir = new Dictionary<string, DirectoryReference>();
				FileReference StagedExecutablePath = GetStagedExecutablePath(Executable.Location, Target.Name);
				DirectoryReference BundleDirectory = Target.bShouldCompileAsDLL ? Executable.Directory.Location : StagedExecutablePath.Directory;
				foreach (UEBuildFramework Framework in BinaryLinkEnvironment.AdditionalFrameworks)
				{
					if (Framework.FrameworkDirectory != null)
					{
						if (!String.IsNullOrEmpty(Framework.CopyBundledAssets))
						{
							// For now, this is hard coded, but we need to loop over all modules, and copy bundled assets that need it
							DirectoryReference LocalSource = DirectoryReference.Combine(Framework.FrameworkDirectory, Framework.CopyBundledAssets);
							string BundleName = Framework.CopyBundledAssets.Substring(Framework.CopyBundledAssets.LastIndexOf('/') + 1);
							FrameworkNameToSourceDir[BundleName] = LocalSource;
						}

						if (Framework.bCopyFramework)
						{
							string FrameworkDir = Framework.FrameworkDirectory.FullName;
							if (FrameworkDir.EndsWith(".framework"))
							{
								FrameworkDir = Path.GetDirectoryName(FrameworkDir);
							}

							OutputFiles.Add(CopyBundleResource(new UEBuildBundleResource(new ModuleRules.BundleResource(Path.Combine(FrameworkDir, Framework.Name + ".framework"), "Frameworks")), Executable, BundleDirectory, Graph));
						}
					}
				}

				foreach (UEBuildBundleResource Resource in BinaryLinkEnvironment.AdditionalBundleResources)
				{
					OutputFiles.Add(CopyBundleResource(Resource, Executable, StagedExecutablePath.Directory, Graph));
				}

				IOSPostBuildSyncTarget PostBuildSyncTarget = new IOSPostBuildSyncTarget(Target, Executable.Location, BinaryLinkEnvironment.IntermediateDirectory, UPLScripts, SdkVersion, FrameworkNameToSourceDir);
				FileReference PostBuildSyncFile = FileReference.Combine(BinaryLinkEnvironment.IntermediateDirectory, "PostBuildSync.dat");
				BinaryFormatterUtils.Save(PostBuildSyncFile, PostBuildSyncTarget);

				string PostBuildSyncArguments = String.Format("-Input=\"{0}\" -XmlConfigCache=\"{1}\" -remoteini=\"{2}\"", PostBuildSyncFile, XmlConfig.CacheFile, UnrealBuildTool.GetRemoteIniPath());

				if (Log.OutputFile != null)
				{
					string LogFileName = Log.OutputFile.GetFileNameWithoutExtension()+"_PostBuildSync.txt";
					LogFileName = FileReference.Combine(Log.OutputFile.Directory, LogFileName).ToString();
					PostBuildSyncArguments += " -log=\"" + LogFileName + "\"";
				}

				Action PostBuildSyncAction = Graph.CreateRecursiveAction<IOSPostBuildSyncMode>(ActionType.CreateAppBundle, PostBuildSyncArguments);
				PostBuildSyncAction.WorkingDirectory = UnrealBuildTool.EngineSourceDirectory;
				PostBuildSyncAction.PrerequisiteItems.Add(Executable);
				PostBuildSyncAction.PrerequisiteItems.AddRange(OutputFiles);
				PostBuildSyncAction.ProducedItems.Add(FileItem.GetItemByFileReference(GetStagedExecutablePath(Executable.Location, Target.Name)));
				PostBuildSyncAction.DeleteItems.AddRange(PostBuildSyncAction.ProducedItems);
				PostBuildSyncAction.StatusDescription = "Executing PostBuildSync";
				PostBuildSyncAction.bCanExecuteRemotely = false;

				OutputFiles.AddRange(PostBuildSyncAction.ProducedItems);
			}

			return OutputFiles;
		}

		public bool ShouldCompileAssetCatalog()
		{
			return Target.Platform == UnrealTargetPlatform.TVOS || (Target.Platform == UnrealTargetPlatform.IOS && Settings.Value.IOSSDKVersionFloat >= 11.0f);
		}

		public static string GetCodesignPlatformName(UnrealTargetPlatform Platform)
		{
			if (Platform == UnrealTargetPlatform.TVOS)
			{
				return "appletvos";
			}
			if (Platform == UnrealTargetPlatform.IOS)
			{
				return "iphoneos";
			}

			throw new BuildException("Invalid platform for GetCodesignPlatformName()");
		}

		class ProcessOutput
		{
			/// <summary>
			/// Substrings that indicate a line contains an error
			/// </summary>
			protected static readonly string[] ErrorMessageTokens =
			{
				"ERROR ",
				"** BUILD FAILED **",
				"[BEROR]",
				"IPP ERROR",
				"System.Net.Sockets.SocketException"
			};

			/// <summary>
			/// Helper function to sync source files to and from the local system and a remote Mac
			/// </summary>
			//This chunk looks to be required to pipe output to VS giving information on the status of a remote build.
			public bool OutputReceivedDataEventHandlerEncounteredError = false;
			public string OutputReceivedDataEventHandlerEncounteredErrorMessage = "";
			public void OutputReceivedDataEventHandler(Object Sender, DataReceivedEventArgs Line)
			{
				if ((Line != null) && (Line.Data != null))
				{
					Log.TraceInformation(Line.Data);

					foreach (string ErrorToken in ErrorMessageTokens)
					{
						if (Line.Data.Contains(ErrorToken))
						{
							OutputReceivedDataEventHandlerEncounteredError = true;
							OutputReceivedDataEventHandlerEncounteredErrorMessage += Line.Data;
							break;
						}
					}
				}
			}

			public void OutputReceivedDataEventLogger(Object Sender, DataReceivedEventArgs Line)
			{
				if ((Line != null) && (Line.Data != null))
				{
					Log.TraceInformation(Line.Data);
				}
			}
		}

		private static void GenerateCrashlyticsData(string DsymZip, string ProjectDir, string ProjectName)
		{
			Log.TraceInformation("Generating and uploading Crashlytics Data");

			// Clean this folder as it's used for extraction
			string TempPath = Path.Combine(UnrealBuildTool.EngineDirectory.FullName, "Intermediate", "Unzipped");

			if (Directory.Exists(TempPath))
			{
				Log.TraceInformation("Deleting temp path {0}", TempPath);
				Directory.Delete(TempPath, true);
			}

			string FabricPath = UnrealBuildTool.EngineDirectory + "/Intermediate/UnzippedFrameworks/Crashlytics/Fabric.embeddedframework";
            if (Directory.Exists(FabricPath) && Environment.GetEnvironmentVariable("IsBuildMachine") == "1")
            {
				//string PlistFile = ProjectDir + "/Intermediate/IOS/" + ProjectName + "-Info.plist";
				Process FabricProcess = new Process();
				FabricProcess.StartInfo.WorkingDirectory = Path.GetDirectoryName(DsymZip);
				FabricProcess.StartInfo.FileName = "/bin/sh";
				FabricProcess.StartInfo.Arguments = string.Format("-c \"chmod 777 \\\"{0}/Fabric.framework/upload-symbols\\\"; \\\"{0}/Fabric.framework/upload-symbols\\\" -a 7a4cebd0324af21696e5e321802c5e26ba541cad -p ios {1}\"",
					FabricPath,
					DsymZip);

				ProcessOutput Output = new ProcessOutput();

				FabricProcess.OutputDataReceived += new DataReceivedEventHandler(Output.OutputReceivedDataEventHandler);
				FabricProcess.ErrorDataReceived += new DataReceivedEventHandler(Output.OutputReceivedDataEventHandler);

				Utils.RunLocalProcess(FabricProcess);
				if (Output.OutputReceivedDataEventHandlerEncounteredError)
				{
					throw new Exception(Output.OutputReceivedDataEventHandlerEncounteredErrorMessage);
				}
			}
		}

		internal static bool GenerateProjectFiles(FileReference ProjectFile, string[] Arguments)
		{
			ProjectFileGenerator.bGenerateProjectFiles = true;
			try
			{
				CommandLineArguments CmdLine = new CommandLineArguments(Arguments);

				PlatformProjectGeneratorCollection PlatformProjectGenerators = new PlatformProjectGeneratorCollection();
				PlatformProjectGenerators.RegisterPlatformProjectGenerator(UnrealTargetPlatform.IOS, new IOSProjectGenerator(CmdLine));
				PlatformProjectGenerators.RegisterPlatformProjectGenerator(UnrealTargetPlatform.TVOS, new TVOSProjectGenerator(CmdLine));

				XcodeProjectFileGenerator Generator = new XcodeProjectFileGenerator(ProjectFile, CmdLine);
				return Generator.GenerateProjectFiles(PlatformProjectGenerators, Arguments);
			}
			finally
			{
				ProjectFileGenerator.bGenerateProjectFiles = false;
			}
		}

		public static FileReference GetStagedExecutablePath(FileReference Executable, string TargetName)
		{
			return FileReference.Combine(Executable.Directory, "Payload", TargetName + ".app", TargetName);
		}

		private static void WriteEntitlements(IOSPostBuildSyncTarget Target)
		{
			string AppName = Target.TargetName;
			FileReference MobileProvisionFile;
			IOSProjectSettings ProjectSettings = ((IOSPlatform)UEBuildPlatform.GetBuildPlatform(Target.Platform)).ReadProjectSettings(Target.ProjectFile);

			if (Target.ImportProvision == null)
			{
				IOSProvisioningData ProvisioningData = ((IOSPlatform)UEBuildPlatform.GetBuildPlatform(Target.Platform)).ReadProvisioningData(ProjectSettings, Target.bForDistribution);
				MobileProvisionFile = ProvisioningData.MobileProvisionFile;
			}
			else
			{
				MobileProvisionFile = new FileReference(Target.ImportProvision);
			}

			string IntermediateDir = (((Target.ProjectFile != null) ? Target.ProjectFile.Directory.ToString() :
				UnrealBuildTool.EngineDirectory.ToString())) + "/Intermediate/" + (Target.Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS");
			// get the settings from the ini file
			ConfigHierarchy Ini = ConfigCache.ReadHierarchy(ConfigHierarchyType.Engine, DirectoryReference.FromFile(Target.ProjectFile), UnrealTargetPlatform.IOS);

			IOSExports.WriteEntitlements(Target.Platform, Ini, AppName, MobileProvisionFile, Target.bForDistribution, IntermediateDir);
		}

		private static void WriteMissingEmbeddedPlist(FileReference MissingPlistPath, DirectoryReference ProjectDirectory, FileReference ProjectFile)
		{
			FileReference PlistTemplatePath = FileReference.Combine(UnrealBuildTool.EngineDirectory, "Build", "IOS", "Resources", "FrameworkPlist", "Embedded.plist");
			if (!DirectoryReference.Exists(MissingPlistPath.Directory))
			{
				DirectoryReference.CreateDirectory(MissingPlistPath.Directory);
			}

			string BundleID = ProjectFiles.Xcode.XcodeFrameworkWrapperUtils.GetBundleID(ProjectDirectory, ProjectFile);
			string BundleName = ProjectFiles.Xcode.XcodeFrameworkWrapperUtils.GetBundleName(ProjectDirectory, ProjectFile);

			BundleID += ".embedded"; // This is necessary because the wrapper that contains this framework cannot have the same BundleID.

			string Plist = FileReference.ReadAllText(PlistTemplatePath);
			Plist = Plist.Replace("BUNDLE_NAME", BundleName);
			Plist = Plist.Replace("BUNDLE_ID", BundleID);

			FileReference.WriteAllText(MissingPlistPath, Plist);
		}

        /// <summary>
        /// If the project is a UE4Game project, Target.ProjectDirectory refers to the engine dir, not the actual dir of the project. So this method gets the 
        /// actual directory of the project whether it is a UE4Game project or not.
        /// </summary>
        /// <returns>The actual project directory.</returns>
        /// <param name="ProjectFile">The path to the project file</param>
        private static DirectoryReference GetActualProjectDirectory(FileReference ProjectFile)
		{
			DirectoryReference ProjectDirectory = (ProjectFile == null ? DirectoryReference.FromString(UnrealBuildTool.GetRemoteIniPath()) : DirectoryReference.FromFile(ProjectFile)); 
			return ProjectDirectory;
		}

		private static void GenerateFrameworkWrapperIfNonexistent(IOSPostBuildSyncTarget Target)
		{
			DirectoryReference ProjectDirectory = GetActualProjectDirectory(Target.ProjectFile);
			DirectoryReference WrapperDirectory = DirectoryReference.Combine(ProjectDirectory, "Wrapper");

			string ProjectName = ProjectDirectory.GetDirectoryName();
			string FrameworkName = Target.TargetName;
			string BundleId = ProjectFiles.Xcode.XcodeFrameworkWrapperUtils.GetBundleID(Target.ProjectDirectory, Target.ProjectFile);
			string EnginePath = UnrealBuildTool.EngineDirectory.ToString();
			string SrcFrameworkPath = DirectoryReference.Combine(Target.ProjectDirectory , "Binaries", "IOS", Target.Configuration.ToString()).ToString(); // We use Target.ProjectDirectory because if it is a UE4Game we want the engine dir and not the actual project dir.
			string CookedDataPath = DirectoryReference.Combine(ProjectDirectory, "Saved", "StagedBuilds", "IOS", "cookeddata").ToString();
			bool GenerateFrameworkWrapperProject = ProjectFiles.Xcode.XcodeFrameworkWrapperUtils.GetGenerateFrameworkWrapperProject(Target.ProjectDirectory);

			string ProvisionName = string.Empty;
			string TeamUUID = string.Empty;
			IOSProjectSettings ProjectSettings = ((IOSPlatform)UEBuildPlatform.GetBuildPlatform(UnrealTargetPlatform.IOS)).ReadProjectSettings(Target.ProjectFile);

			IOSProvisioningData Data = ((IOSPlatform)UEBuildPlatform.GetBuildPlatform(UnrealTargetPlatform.IOS)).ReadProvisioningData(ProjectSettings, Target.bForDistribution);
			if (Data != null)
			{
				ProvisionName = Data.MobileProvisionName;
				TeamUUID = Data.TeamUUID;
			}

			// Create the framework wrapper project, only if it doesn't exist and only if the user wants to generate the wrapper.
			if (!DirectoryReference.Exists(DirectoryReference.Combine(WrapperDirectory, FrameworkName)) && GenerateFrameworkWrapperProject)
			{
				ProjectFiles.Xcode.XcodeFrameworkWrapperProject.GenerateXcodeFrameworkWrapper(WrapperDirectory.ToString(), ProjectName, FrameworkName, BundleId, SrcFrameworkPath, EnginePath, CookedDataPath, ProvisionName, TeamUUID);
			}
		}

		public static void PostBuildSync(IOSPostBuildSyncTarget Target)
		{
			ConfigHierarchy Ini = ConfigCache.ReadHierarchy(ConfigHierarchyType.Engine, GetActualProjectDirectory(Target.ProjectFile), UnrealTargetPlatform.IOS);
			string BundleID;
			Ini.GetString("/Script/IOSRuntimeSettings.IOSRuntimeSettings", "BundleIdentifier", out BundleID);

			IOSProjectSettings ProjectSettings = ((IOSPlatform)UEBuildPlatform.GetBuildPlatform(Target.Platform)).ReadProjectSettings(Target.ProjectFile);

			bool bPerformFullAppCreation = true;
			string PathToDsymZip = Target.OutputPath.FullName + ".dSYM.zip";
			if (BuildHostPlatform.Current.Platform == UnrealTargetPlatform.Mac)
			{
				if (Target.bBuildAsFramework)
				{
					// make sure the framework has a plist
					FileReference PlistSrcLocation = FileReference.Combine(Target.ProjectDirectory, "Build", "IOS", "Embedded.plist");
					if (!FileReference.Exists(PlistSrcLocation))
					{

						WriteMissingEmbeddedPlist(PlistSrcLocation, Target.ProjectDirectory, Target.ProjectFile);
						if (!FileReference.Exists(PlistSrcLocation))
							throw new BuildException("Unable to find plist for output framework ({0})", PlistSrcLocation);
					}

					FileReference PlistDstLocation = FileReference.Combine(Target.OutputPath.Directory, "Info.plist");
					if (FileReference.Exists(PlistDstLocation))
					{
						FileReference.SetAttributes(PlistDstLocation, FileAttributes.Normal);
					}

					FileReference.Copy(PlistSrcLocation, PlistDstLocation, true);

					PathToDsymZip = Path.Combine(Target.OutputPath.Directory.ParentDirectory.FullName, Target.OutputPath.GetFileName() + ".dSYM.zip");

					GenerateFrameworkWrapperIfNonexistent(Target);

					// do not perform any of the .app creation below
					bPerformFullAppCreation = false;
				}
			}

			string AppName = Target.TargetName;

			if (!Target.bSkipCrashlytics)
			{
				GenerateCrashlyticsData(PathToDsymZip, Target.ProjectDirectory.FullName, AppName);
			}

			// only make the app if needed
			if (!bPerformFullAppCreation)
			{
				return;
			}

			// copy the executable
			FileReference StagedExecutablePath = GetStagedExecutablePath(Target.OutputPath, Target.TargetName);
			DirectoryReference.CreateDirectory(StagedExecutablePath.Directory);
			FileReference.Copy(Target.OutputPath, StagedExecutablePath, true);
			string RemoteShadowDirectoryMac = Target.OutputPath.Directory.FullName;

			if (Target.bCreateStubIPA || Target.bBuildAsFramework)
			{
				string FrameworkDerivedDataDir = Path.GetFullPath(Target.ProjectDirectory + "/Intermediate/" + (Target.Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS") + "/DerivedData");
				DirectoryReference FrameworkPayloadDirectory = DirectoryReference.Combine(Target.OutputPath.Directory.ParentDirectory.ParentDirectory, "Payload");

				// generate the dummy project so signing works
				DirectoryReference XcodeWorkspaceDir = null;
                if (!Target.bBuildAsFramework)
                {
					if (AppName == "UE4Game" || AppName == "UE4Client" || Target.ProjectFile == null || Target.ProjectFile.IsUnderDirectory(UnrealBuildTool.EngineDirectory))
					{
						XcodeWorkspaceDir = DirectoryReference.Combine(UnrealBuildTool.RootDirectory, String.Format("UE4_{0}.xcworkspace", (Target.Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS")));
					}
					else
					{
						XcodeWorkspaceDir = DirectoryReference.Combine(Target.ProjectDirectory, String.Format("{0}_{1}.xcworkspace", Target.ProjectFile.GetFileNameWithoutExtension(), (Target.Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS")));
					}
                }

				// Path to the temporary keychain. When -ImportCertificate is specified, we will temporarily add this to the list of keychains to search, and remove it later.
				FileReference TempKeychain = FileReference.Combine(Target.ProjectIntermediateDirectory, "TempKeychain.keychain");

				FileReference SignProjectScript = FileReference.Combine(Target.ProjectIntermediateDirectory, "SignProject.sh");
				using (StreamWriter Writer = new StreamWriter(SignProjectScript.FullName))
				{
					// Boilerplate
					Writer.WriteLine("#!/bin/sh");
					Writer.WriteLine("set -e");
					Writer.WriteLine("set -x");

					// Copy the mobile provision into the system store
					if (Target.ImportProvision != null)
					{
						Writer.WriteLine("cp -f {0} ~/Library/MobileDevice/Provisioning\\ Profiles/", Utils.EscapeShellArgument(Target.ImportProvision));
					}

					// Get the signing certificate to use
					string SigningCertificate;
					if (Target.ImportCertificate == null)
					{
						// Take it from the standard settings
						IOSProvisioningData ProvisioningData = ((IOSPlatform)UEBuildPlatform.GetBuildPlatform(Target.Platform)).ReadProvisioningData(Target.ProjectFile, Target.bForDistribution);
						SigningCertificate = ProvisioningData.SigningCertificate;

						// Set the identity on the command line
						if (!ProjectSettings.bAutomaticSigning)
						{
							Writer.WriteLine("CODE_SIGN_IDENTITY='{0}'", String.IsNullOrEmpty(SigningCertificate) ? "IPhoneDeveloper" : SigningCertificate);
						}
					}
					else
					{
						// Read the name from the certificate
						X509Certificate2 Certificate;
						try
						{
							Certificate = new X509Certificate2(Target.ImportCertificate, Target.ImportCertificatePassword ?? "");
						}
						catch (Exception Ex)
						{
							throw new BuildException(Ex, "Unable to read certificate '{0}': {1}", Target.ImportCertificate, Ex.Message);
						}
						SigningCertificate = Certificate.GetNameInfo(X509NameType.SimpleName, false);

						// Install a certificate given on the command line to a temporary keychain
						Writer.WriteLine("security delete-keychain \"{0}\" || true", TempKeychain);
						Writer.WriteLine("security create-keychain -p \"A\" \"{0}\"", TempKeychain);
						Writer.WriteLine("security list-keychains -s \"{0}\"", TempKeychain);
						Writer.WriteLine("security list-keychains");
						Writer.WriteLine("security set-keychain-settings -t 3600 -l  \"{0}\"", TempKeychain);
						Writer.WriteLine("security -v unlock-keychain -p \"A\" \"{0}\"", TempKeychain);
						Writer.WriteLine("security import {0} -P {1} -k \"{2}\" -T /usr/bin/codesign -T /usr/bin/security -t agg", Utils.EscapeShellArgument(Target.ImportCertificate), Utils.EscapeShellArgument(Target.ImportCertificatePassword), TempKeychain);
						Writer.WriteLine("security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k \"A\" -D '{0}' -t private {1}", SigningCertificate, TempKeychain);

						// Set parameters to make sure it uses the correct identity and keychain
						Writer.WriteLine("CERT_IDENTITY='{0}'", SigningCertificate);
						Writer.WriteLine("CODE_SIGN_IDENTITY='{0}'", SigningCertificate);
						Writer.WriteLine("CODE_SIGN_KEYCHAIN='{0}'", TempKeychain);
					}

					FileReference MobileProvisionFile;
					string MobileProvisionUUID;
					string TeamUUID;
					if (Target.ImportProvision == null)
					{
						IOSProvisioningData ProvisioningData = ((IOSPlatform)UEBuildPlatform.GetBuildPlatform(Target.Platform)).ReadProvisioningData(ProjectSettings, Target.bForDistribution);
						MobileProvisionFile = ProvisioningData.MobileProvisionFile;
						MobileProvisionUUID = ProvisioningData.MobileProvisionUUID;
						TeamUUID = ProvisioningData.TeamUUID;
						if (ProvisioningData.BundleIdentifier == null || !ProvisioningData.BundleIdentifier.Contains("*"))
						{
							// If the BundleIndentifer contains a wild card it will not be valid to use in the plist.
							BundleID = ProvisioningData.BundleIdentifier;
						}
					}
					else
					{
						MobileProvisionFile = new FileReference(Target.ImportProvision);

						MobileProvisionContents MobileProvision = MobileProvisionContents.Read(MobileProvisionFile);
						MobileProvisionUUID = MobileProvision.GetUniqueId();
						MobileProvision.TryGetTeamUniqueId(out TeamUUID);
						string BundleIdentifier = MobileProvision.GetBundleIdentifier();
						if (!BundleIdentifier.Contains("*"))
						{
							// If the BundleIndentifer contains a wild card it will not be valid to use in the plist.
							BundleID = BundleIdentifier;
						}
					}

					if (MobileProvisionFile == null)
					{
						throw new BuildException("Unable to find valid certificate/mobile provision pair.");
					}

					string ConfigName = Target.Configuration.ToString();
					if (Target.TargetType != TargetType.Game && Target.TargetType != TargetType.Program)
					{
						ConfigName += " " + Target.TargetType.ToString();
					}

					string SchemeName;
					if (AppName == "UE4Game" || AppName == "UE4Client")
					{
						if (Target.bBuildAsFramework)
						{
							SchemeName = "UE4Game";
						}
						else
						{
							SchemeName = "UE4";
						}
					}
					else
					{
						SchemeName = Target.ProjectFile.GetFileNameWithoutExtension();
					}

					Console.WriteLine("Provisioning: {0}, {1}, {2}, {3}", MobileProvisionFile, MobileProvisionFile.GetFileName(), MobileProvisionUUID, BundleID);

					string CmdLine;
					if (Target.bBuildAsFramework)
					{
						DirectoryReference FrameworkProjectDirectory = GetActualProjectDirectory(Target.ProjectFile);
						DirectoryReference WrapperDirectory = DirectoryReference.Combine(FrameworkProjectDirectory, "Wrapper");
						string FrameworkName = Target.TargetName;
						DirectoryReference WrapperProject = DirectoryReference.Combine(WrapperDirectory, FrameworkName, FrameworkName + ".xcodeproj");

						// Delete everything in payload directory before building
						Writer.WriteLine(String.Format("rm -rf \"{0}\"", FrameworkPayloadDirectory));

						// Build the framework wrapper
						CmdLine = new IOSToolChainSettings().XcodeDeveloperDir + "usr/bin/xcodebuild" +
							" -project \"" + WrapperProject + "\"" +
								" -configuration \"" + ConfigName + "\"" +
							" -scheme '" + SchemeName + "'" +
								" -sdk " + GetCodesignPlatformName(Target.Platform) +
							" -destination generic/platform=" + (Target.Platform == UnrealTargetPlatform.IOS ? "iOS" : "tvOS") +
								" -derivedDataPath \"" + FrameworkDerivedDataDir + "\"" +
							" CONFIGURATION_BUILD_DIR=\"" + FrameworkPayloadDirectory + "\"" +
								(!string.IsNullOrEmpty(TeamUUID) ? " DEVELOPMENT_TEAM=" + TeamUUID : "");
					}
					else
					{
						// code sign the project
						CmdLine = new IOSToolChainSettings().XcodeDeveloperDir + "usr/bin/xcodebuild" +
							" -workspace \"" + XcodeWorkspaceDir + "\"" +
								" -configuration \"" + ConfigName + "\"" +
							" -scheme '" + SchemeName + "'" +
								" -sdk " + GetCodesignPlatformName(Target.Platform) +
							" -destination generic/platform=" + (Target.Platform == UnrealTargetPlatform.IOS ? "iOS" : "tvOS") +
								(!string.IsNullOrEmpty(TeamUUID) ? " DEVELOPMENT_TEAM=" + TeamUUID : "");
					}

					CmdLine += String.Format(" CODE_SIGN_IDENTITY='{0}'", SigningCertificate);

					if (!ProjectSettings.bAutomaticSigning)
					{
						CmdLine += (!string.IsNullOrEmpty(MobileProvisionUUID) ? (" PROVISIONING_PROFILE_SPECIFIER=" + MobileProvisionUUID) : "");
					}
					Writer.WriteLine("/usr/bin/xcrun {0}", CmdLine);
				}

				if (!Target.bBuildAsFramework)
				{
				    if (AppName == "UE4Game" || AppName == "UE4Client" || Target.ProjectFile == null || Target.ProjectFile.IsUnderDirectory(UnrealBuildTool.EngineDirectory))
				    {
					    GenerateProjectFiles(Target.ProjectFile, new string[] { "-platforms=" + (Target.Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS"), "-NoIntellIsense", (Target.Platform == UnrealTargetPlatform.IOS ? "-iosdeployonly" : "-tvosdeployonly"), "-ignorejunk", (Target.bForDistribution ? "-distribution" : "-development"), "-bundleID=" + BundleID, "-includetemptargets", "-appname=" + AppName });
				    }
				    else
				    {
					    GenerateProjectFiles(Target.ProjectFile, new string[] { "-platforms=" + (Target.Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS"), "-NoIntellIsense", (Target.Platform == UnrealTargetPlatform.IOS ? "-iosdeployonly" : "-tvosdeployonly"), "-ignorejunk", (Target.bForDistribution ? "-distribution" : "-development"), String.Format("-project={0}", Target.ProjectFile), "-game", "-bundleID=" + BundleID, "-includetemptargets" });
				    }
				    // Make sure it exists
				    if (!DirectoryReference.Exists(XcodeWorkspaceDir))
				    {
					    throw new BuildException("Unable to create stub IPA; Xcode workspace not found at {0}", XcodeWorkspaceDir);
				    }
				}

				// ensure the plist, entitlements, and provision files are properly copied
				UEDeployIOS DeployHandler = (Target.Platform == UnrealTargetPlatform.IOS ? new UEDeployIOS() : new UEDeployTVOS());
				DeployHandler.ForDistribution = Target.bForDistribution;
				DeployHandler.PrepTargetForDeployment(Target.ProjectFile, Target.TargetName, Target.Platform, Target.Configuration, Target.UPLScripts, Target.SdkVersion, Target.bCreateStubIPA, BundleID, Target.bBuildAsFramework);

				Log.TraceInformation("Executing {0}", SignProjectScript);

				// write the entitlements file (building remotely)
				WriteEntitlements(Target);

				Process SignProcess = new Process();
				SignProcess.StartInfo.WorkingDirectory = RemoteShadowDirectoryMac;
				SignProcess.StartInfo.FileName = "/bin/sh";
				string Arguments = String.Format("\"{0}\"", SignProjectScript.FullName);
				SignProcess.StartInfo.Arguments = Arguments;

				ProcessOutput Output = new ProcessOutput();

				SignProcess.OutputDataReceived += new DataReceivedEventHandler(Output.OutputReceivedDataEventHandler);
				SignProcess.ErrorDataReceived += new DataReceivedEventHandler(Output.OutputReceivedDataEventHandler);

				Output.OutputReceivedDataEventHandlerEncounteredError = false;
				Output.OutputReceivedDataEventHandlerEncounteredErrorMessage = "";
				Utils.RunLocalProcess(SignProcess);

				// cleanup
				if (Target.ImportCertificate != null)
				{
					FileReference CleanProjectScript = FileReference.Combine(Target.ProjectIntermediateDirectory, "CleanProject.sh");
					using (StreamWriter CleanWriter = new StreamWriter(CleanProjectScript.FullName))
					{
						// Remove the temporary keychain from the search list
						CleanWriter.WriteLine("security delete-keychain \"{0}\" || true", TempKeychain);
						CleanWriter.WriteLine("security list-keychain -s login.keychain");
					}

					Log.TraceInformation("Executing {0}", CleanProjectScript);

					Process CleanProcess = new Process();
					CleanProcess.StartInfo.WorkingDirectory = RemoteShadowDirectoryMac;
					CleanProcess.StartInfo.FileName = "/bin/sh";
					CleanProcess.StartInfo.Arguments = CleanProjectScript.FullName;

					ProcessOutput CleanOutput = new ProcessOutput();

					SignProcess.OutputDataReceived += new DataReceivedEventHandler(CleanOutput.OutputReceivedDataEventLogger);
					SignProcess.ErrorDataReceived += new DataReceivedEventHandler(CleanOutput.OutputReceivedDataEventLogger);

					Utils.RunLocalProcess(CleanProcess);
				}

				if (Target.bBuildAsFramework)
				{
					CleanIntermediateDirectory(FrameworkDerivedDataDir);
				}

				if (XcodeWorkspaceDir != null)
				{
					// delete the temp project
					DirectoryReference.Delete(XcodeWorkspaceDir, true);

					if (Output.OutputReceivedDataEventHandlerEncounteredError)
					{
						throw new Exception(Output.OutputReceivedDataEventHandlerEncounteredErrorMessage);
					}
				}

				// Package the stub
				if (Target.bBuildAsFramework)
				{
					PackageStub(FrameworkPayloadDirectory.ParentDirectory.ToString(), AppName, Target.OutputPath.GetFileNameWithoutExtension());
				}
				else
				{
					PackageStub(RemoteShadowDirectoryMac, AppName, Target.OutputPath.GetFileNameWithoutExtension());
				}
			}
			else
			{
				// ensure the plist, entitlements, and provision files are properly copied
				UEDeployIOS DeployHandler = (Target.Platform == UnrealTargetPlatform.IOS ? new UEDeployIOS() : new UEDeployTVOS());
				DeployHandler.ForDistribution = Target.bForDistribution;
				DeployHandler.PrepTargetForDeployment(Target.ProjectFile, Target.TargetName, Target.Platform, Target.Configuration, Target.UPLScripts, Target.SdkVersion, Target.bCreateStubIPA, BundleID, Target.bBuildAsFramework);

				// write the entitlements file (building on Mac)
				WriteEntitlements(Target);
			}

			{
				// Copy bundled assets from additional frameworks to the intermediate assets directory (so they can get picked up during staging)
				String LocalFrameworkAssets = Path.GetFullPath(Target.ProjectDirectory + "/Intermediate/" + (Target.Platform == UnrealTargetPlatform.IOS ? "IOS" : "TVOS") + "/FrameworkAssets");

				// Clean the local dest directory if it exists
				CleanIntermediateDirectory(LocalFrameworkAssets);

				foreach (KeyValuePair<string, DirectoryReference> Pair in Target.FrameworkNameToSourceDir)
				{
					//string UnpackedZipPath = Pair.Value.FullName;

					// For now, this is hard coded, but we need to loop over all modules, and copy bundled assets that need it
					string LocalDest = LocalFrameworkAssets + "/" + Pair.Key;

					Log.TraceInformation("Copying bundled asset... LocalSource: {0}, LocalDest: {1}", Pair.Value, LocalDest);

					string ResultsText;
					RunExecutableAndWait("cp", String.Format("-R -L \"{0}\" \"{1}\"", Pair.Value, LocalDest), out ResultsText);
				}
			}
		}

		public static int RunExecutableAndWait(string ExeName, string ArgumentList, out string StdOutResults)
		{
			// Create the process
			ProcessStartInfo PSI = new ProcessStartInfo(ExeName, ArgumentList);
			PSI.RedirectStandardOutput = true;
			PSI.UseShellExecute = false;
			PSI.CreateNoWindow = true;
			Process NewProcess = Process.Start(PSI);

			// Wait for the process to exit and grab it's output
			StdOutResults = NewProcess.StandardOutput.ReadToEnd();
			NewProcess.WaitForExit();
			return NewProcess.ExitCode;
		}

		public void StripSymbols(FileReference SourceFile, FileReference TargetFile)
		{
			StripSymbolsWithXcode(SourceFile, TargetFile, Settings.Value.ToolchainDir);
		}

		FileItem CopyBundleResource(UEBuildBundleResource Resource, FileItem Executable, DirectoryReference BundleDirectory, IActionGraphBuilder Graph)
		{
			Action CopyAction = Graph.CreateAction(ActionType.CreateAppBundle);
			CopyAction.WorkingDirectory = GetMacDevSrcRoot(); // Path.GetFullPath(".");
			CopyAction.CommandPath = BuildHostPlatform.Current.Shell;
			CopyAction.CommandDescription = "";

			string BundlePath = BundleDirectory.FullName;
			string SourcePath = Path.Combine(Path.GetFullPath("."), Resource.ResourcePath);
			string TargetPath;
			if (Resource.BundleContentsSubdir == "Resources")
			{
				TargetPath = Path.Combine(BundlePath, Path.GetFileName(Resource.ResourcePath));
			}
			else
			{
				TargetPath = Path.Combine(BundlePath, Resource.BundleContentsSubdir, Path.GetFileName(Resource.ResourcePath));
			}

			FileItem TargetItem = FileItem.GetItemByPath(TargetPath);

			CopyAction.CommandArguments = string.Format("-c \"cp -f -R \\\"{0}\\\" \\\"{1}\\\"; touch -c \\\"{2}\\\"\"", SourcePath, Path.GetDirectoryName(TargetPath).Replace('\\', '/') + "/", TargetPath.Replace('\\', '/'));
			CopyAction.PrerequisiteItems.Add(Executable);
			CopyAction.ProducedItems.Add(TargetItem);
			CopyAction.bShouldOutputStatusDescription = Resource.bShouldLog;
			CopyAction.StatusDescription = string.Format("Copying {0} to app bundle", Path.GetFileName(Resource.ResourcePath));
			CopyAction.bCanExecuteRemotely = false;

			return TargetItem;
		}

		public override void SetupBundleDependencies(List<UEBuildBinary> Binaries, string GameName)
		{
			base.SetupBundleDependencies(Binaries, GameName);

			foreach (UEBuildBinary Binary in Binaries)
			{
				BundleDependencies.Add(FileItem.GetItemByFileReference(Binary.OutputFilePath));
			}
		}

	};
}
