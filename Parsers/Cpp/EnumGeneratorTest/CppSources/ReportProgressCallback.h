#ifndef REPORT_SENDER_PROGRESS_CALLBACK_H
#define REPORT_SENDER_PROGRESS_CALLBACK_H

#include <unknwn.h>
#include <atlbase.h>
#include <stdint.h>

enum class ReportStage : int32_t
{
    Unknown = -1,
    Done = 0,
    Preparing,
    MakingZip,
    SendingHttp,
    Error,
};


enum class ReportStopReason : int8_t
{
    Unknown = -1,
    Success = 0,
    Canceled = 1,
    ArchiveTooBig = 2,
    SendError = 3,
    CantCreateArchive = 4,
    CantResolveProxy = 5,
    ProxyWrongCrendentials = 6,
    OutOfSpace = 7,
    OtherNetworkError = 8
};

#pragma pack(push, 8)

struct Credentials
{
    BSTR encryptedUserName;
    BSTR encryptedPassword;
};

#pragma pack(pop)

struct __declspec(uuid("{ABABABA3-4D8D-4152-ACC7-4F9548C156F0}"))
    ATL_NO_VTABLE IReportProgress : public ::IUnknown
{
    STDMETHOD_(void, ProgressChanged)(uint32_t percent) = 0;
    STDMETHOD_(void, StateChanged)(ReportStage currentStage) = 0;
    STDMETHOD_(void, Stopped)(ReportStopReason reason) = 0;
    STDMETHOD_(bool, ProxyCredentialsRequired)(Credentials* credentials) = 0;
};

#endif // REPORT_SENDER_PROGRESS_CALLBACK_H
