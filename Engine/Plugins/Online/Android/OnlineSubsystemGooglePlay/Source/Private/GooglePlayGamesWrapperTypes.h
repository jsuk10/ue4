// Copyright Epic Games, Inc. All Rights Reserved.

#pragma once


DECLARE_DELEGATE_TwoParams(FGooglePlayGamesWrapperLoginComplete, const FString& /*UserId*/, const FString& /*PlayerDisplayName*/, const FString& /*AuthCode*/);

class FGooglePlayGamesWrapperReadLeaderboardResult
{
};
class FGooglePlayGamesWrapperFlushLeaderboards
{
};
class FGooglePlayGamesWrapperQueryAchievementsResult
{
};

class FGooglePlayGamesWrapperWriteAchievementsResult
{
};


class FGooglePlayGamesWrapper
{
public:
	FGooglePlayGamesWrapper() = default;
	FGooglePlayGamesWrapper(const FGooglePlayGamesWrapper& Other) = delete;
	FGooglePlayGamesWrapper& operator=(const FGooglePlayGamesWrapper& Other) = delete;
	~FGooglePlayGamesWrapper();

	void Init();
	void Reset();

	bool Login(FOnlineAsyncTaskGooglePlayLogin* Task, const FString& InAuthCodeClientId, bool InForceRefreshToken);
	bool RequestLeaderboardScore(FOnlineAsyncTaskGooglePlayReadLeaderboard* Task, const FString& LeaderboardId);
	bool FlushLeaderboardsScores(FOnlineAsyncTaskGooglePlayFlushLeaderboards* Task, const TArray<FGooglePlayLeaderboardScore>& Scores);
	bool QueryAchievements(FOnlineAsyncTaskGooglePlayQueryAchievements* Task);
	bool WriteAchievements(FOnlineAsyncTaskGooglePlayWriteAchievements* Task, const TArray<FGooglePlayAchievementWriteData>& WriteAchievementsData);
	bool ShowAchievementsUI();
	bool ShowLeaderboardUI(const FString& LeaderboardName);

private:
	jclass PlayGamesWrapperClass = nullptr;
	jmethodID LoginMethodId = nullptr;
	jmethodID RequestLeaderboardScoreId = nullptr;
	jmethodID SubmitLeaderboardsScoresId = nullptr;
	jmethodID QueryAchievementsId = nullptr;
	jmethodID WriteAchievementsId = nullptr;
	jmethodID ShowAchievementsUIId = nullptr;
	jmethodID ShowLeaderboardUIId = nullptr;
};
