// Copyright Epic Games, Inc. All Rights Reserved.

#include "AndroidRuntimeSettings.h"
#include "Modules/ModuleManager.h"
#include "UObject/UnrealType.h"
#include "Misc/ConfigCacheIni.h"
#include "Misc/CoreDelegates.h"
#include "HAL/IConsoleManager.h"
#include "Engine/RendererSettings.h"
#include "HAL/PlatformApplicationMisc.h"

#if WITH_EDITOR
#include "IAndroidTargetPlatformModule.h"
#endif

DEFINE_LOG_CATEGORY(LogAndroidRuntimeSettings);

UAndroidRuntimeSettings::UAndroidRuntimeSettings(const FObjectInitializer& ObjectInitializer)
	: Super(ObjectInitializer)
	, Orientation(EAndroidScreenOrientation::Landscape)
	, MaxAspectRatio(2.1f)
	, bAndroidVoiceEnabled(false)
	, GoogleVRCaps({EGoogleVRCaps::Daydream33})
	, bEnableGooglePlaySupport(false)
	, RequestCodeForPlayGamesActivities(80002)
	, bForceRefreshToken(false)
	, bSupportAdMob(true)
	, bBlockAndroidKeysOnControllers(false)
	, AudioSampleRate(44100)
	, AudioCallbackBufferFrameSize(1024)
	, AudioNumBuffersToEnqueue(4)
	, bMultiTargetFormat_ETC2(true)
	, bMultiTargetFormat_DXT(true)
	, bMultiTargetFormat_ASTC(true)
	, TextureFormatPriority_ETC2(0.2f)
	, TextureFormatPriority_DXT(0.6f)
	, TextureFormatPriority_ASTC(0.9f)
	, bStreamLandscapeMeshLODs(false)
{
	bBuildForES31 = bBuildForES31 || !bSupportsVulkan;
}

void UAndroidRuntimeSettings::PostReloadConfig(FProperty* PropertyThatWasLoaded)
{
	Super::PostReloadConfig(PropertyThatWasLoaded);

#if PLATFORM_ANDROID

	FPlatformApplicationMisc::SetGamepadsAllowed(bAllowControllers);

#endif //PLATFORM_ANDROID
}

#if WITH_EDITOR

void UAndroidRuntimeSettings::HandlesRGBHWSupport()
{
	const bool SupportssRGB = PackageForOculusMobile.Num() > 0;
	URendererSettings* const Settings = GetMutableDefault<URendererSettings>();
	static auto* MobileUseHWsRGBEncodingCVAR = IConsoleManager::Get().FindConsoleVariable(TEXT("r.Mobile.UseHWsRGBEncoding"));

	if (SupportssRGB != Settings->bMobileUseHWsRGBEncoding)
	{
		Settings->bMobileUseHWsRGBEncoding = SupportssRGB;
		Settings->UpdateSinglePropertyInConfigFile(Settings->GetClass()->FindPropertyByName(GET_MEMBER_NAME_CHECKED(URendererSettings, bMobileUseHWsRGBEncoding)), GetDefaultConfigFilename());
	}

	if (MobileUseHWsRGBEncodingCVAR && MobileUseHWsRGBEncodingCVAR->GetInt() != (int)SupportssRGB)
	{
		MobileUseHWsRGBEncodingCVAR->Set((int)SupportssRGB);
	}

}

static void InvalidateAllAndroidPlatforms()
{
	ITargetPlatformModule* Module = FModuleManager::GetModulePtr<IAndroidTargetPlatformModule>("AndroidTargetPlatform");

	// call the delegate for each TP object
	for (ITargetPlatform* TargetPlatform : Module->GetTargetPlatforms())
	{
		FCoreDelegates::OnTargetPlatformChangedSupportedFormats.Broadcast(TargetPlatform);
	}
}

void UAndroidRuntimeSettings::PostEditChangeProperty(struct FPropertyChangedEvent& PropertyChangedEvent)
{
	Super::PostEditChangeProperty(PropertyChangedEvent);

	// Ensure that at least one architecture is supported
	if (!bBuildForArmV7 && !bBuildForX8664 && !bBuildForArm64)
	{
		bBuildForArmV7 = true;
		UpdateSinglePropertyInConfigFile(GetClass()->FindPropertyByName(GET_MEMBER_NAME_CHECKED(UAndroidRuntimeSettings, bBuildForArmV7)), GetDefaultConfigFilename());
	}

	if (PropertyChangedEvent.Property != nullptr)
	{
		if (PropertyChangedEvent.Property->GetFName() == GET_MEMBER_NAME_CHECKED(UAndroidRuntimeSettings, bSupportsVulkan) ||
			PropertyChangedEvent.Property->GetFName() == GET_MEMBER_NAME_CHECKED(UAndroidRuntimeSettings, bBuildForES31))
		{
			// Supported shader formats changed so invalidate cache
			InvalidateAllAndroidPlatforms();

			OnPropertyChanged.Broadcast(PropertyChangedEvent);
		}
	}

	EnsureValidGPUArch();

	if (PropertyChangedEvent.Property != nullptr && PropertyChangedEvent.Property->GetName().StartsWith(TEXT("bMultiTargetFormat")))
	{
		UpdateSinglePropertyInConfigFile(PropertyChangedEvent.Property, GetDefaultConfigFilename());

		// Ensure we have at least one format for Android_Multi
		if (!bMultiTargetFormat_ETC2 && !bMultiTargetFormat_DXT && !bMultiTargetFormat_ASTC)
		{
			bMultiTargetFormat_ETC2 = true;
			UpdateSinglePropertyInConfigFile(GetClass()->FindPropertyByName(GET_MEMBER_NAME_CHECKED(UAndroidRuntimeSettings, bMultiTargetFormat_ETC2)), GetDefaultConfigFilename());
		}

		// Notify the AndroidTargetPlatform module if it's loaded
		IAndroidTargetPlatformModule* Module = FModuleManager::GetModulePtr<IAndroidTargetPlatformModule>("AndroidTargetPlatform");
		if (Module)
		{
			Module->NotifyMultiSelectedFormatsChanged();
		}
	}

	if (PropertyChangedEvent.Property != nullptr && PropertyChangedEvent.Property->GetName().StartsWith(TEXT("TextureFormatPriority")))
	{
		UpdateSinglePropertyInConfigFile(PropertyChangedEvent.Property, GetDefaultConfigFilename());

		// Notify the AndroidTargetPlatform module if it's loaded
		IAndroidTargetPlatformModule* Module = FModuleManager::GetModulePtr<IAndroidTargetPlatformModule>("AndroidTargetPlatform");
		if (Module)
		{
			Module->NotifyMultiSelectedFormatsChanged();
		}
	}

	if (PropertyChangedEvent.Property != nullptr && PropertyChangedEvent.Property->GetName().StartsWith(TEXT("PackageForOculusMobile")))
	{
		if (PropertyChangedEvent.ChangeType == EPropertyChangeType::ArrayAdd)
		{
			// Get a list of all available devices
			TArray<EOculusMobileDevice::Type> deviceList;
#define OCULUS_DEVICE_LOOP(device) deviceList.Add(device);
			FOREACH_ENUM_EOCULUSMOBILEDEVICE(OCULUS_DEVICE_LOOP);
#undef OCULUS_DEVICE_LOOP
			// Add last device that isn't already in the list
			for (int i = deviceList.Num() - 1; i >= 0; --i)
			{
				if (!PackageForOculusMobile.Contains(deviceList[i]))
				{
					PackageForOculusMobile.Last() = deviceList[i];
					break;
				}
			}
		}
	}

	HandlesRGBHWSupport();
}

void UAndroidRuntimeSettings::PostInitProperties()
{
	Super::PostInitProperties();

	// If the config has an AdMobAdUnitID then we migrate it on load and clear the value
	if (!AdMobAdUnitID.IsEmpty())
	{
		AdMobAdUnitIDs.Add(AdMobAdUnitID);
		AdMobAdUnitID.Empty();
		UpdateDefaultConfigFile();
	}

	// Upgrade old GoogleVR settings as necessary.
	FString GoogleVRMode = GConfig->GetStr(TEXT("/Script/AndroidRuntimeSettings.AndroidRuntimeSettings"), TEXT("GoogleVRMode"), GEngineIni);
	if (GoogleVRMode != TEXT(""))
	{
		if (GoogleVRMode == TEXT("Cardboard"))
		{
			GoogleVRCaps.Empty(1);
			GoogleVRCaps.Add(EGoogleVRCaps::Cardboard);
			UE_LOG(LogAndroidRuntimeSettings, Log, TEXT("Upgraded GoogleVRMode -> GoogleVRCaps, Cardboard"));
		}
		else if (GoogleVRMode == TEXT("Daydream"))
		{
			GoogleVRCaps.Empty(1);
			GoogleVRCaps.Add(EGoogleVRCaps::Daydream33);
			UE_LOG(LogAndroidRuntimeSettings, Log, TEXT("Upgraded GoogleVRMode -> GoogleVRCaps, Daydream"));
		}
		else if (GoogleVRMode == TEXT("DaydreamAndCardboard"))
		{
			GoogleVRCaps.Empty(2);
			GoogleVRCaps.Add(EGoogleVRCaps::Cardboard);
			GoogleVRCaps.Add(EGoogleVRCaps::Daydream33);
			UE_LOG(LogAndroidRuntimeSettings, Log, TEXT("Upgraded GoogleVRMode -> GoogleVRCaps, Cardboard & Daydream"));
		}

		// Save changes to the ini file.
		UpdateDefaultConfigFile();
	}

	EnsureValidGPUArch();
	HandlesRGBHWSupport();
}

void UAndroidRuntimeSettings::EnsureValidGPUArch()
{
	// Ensure that at least one GPU architecture is supported
	if (!bSupportsVulkan && !bBuildForES31 && !bSupportsVulkanSM5)
	{
		bBuildForES31 = true;
		UpdateSinglePropertyInConfigFile(GetClass()->FindPropertyByName(GET_MEMBER_NAME_CHECKED(UAndroidRuntimeSettings, bBuildForES31)), GetDefaultConfigFilename());

		// Supported shader formats changed so invalidate cache
		InvalidateAllAndroidPlatforms();
	}
}
#endif
