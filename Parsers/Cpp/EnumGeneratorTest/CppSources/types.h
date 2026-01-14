#ifndef PRODUCT_PLATFORM_IFACE_SUPPORT_TOOLS_TYPES_H
#define PRODUCT_PLATFORM_IFACE_SUPPORT_TOOLS_TYPES_H

#include <component/xyz/rtl/types.h>
#include <component/xyz/rtl/string.h>
#include <component/xyz/rtl/enum_value.h>

namespace product_platform
{

namespace support_tools
{

namespace error_scenario
{

enum Enum
{
    CrashOrFreeze,
    WebPageFailure,
    ActivationFailure,
    Other
};
typedef xyz::enum_value_t<Enum> Type;

} // namespace error_scenario

namespace tool_state
{

enum Enum
{
    ToolNotRunning,
    ToolRunning,
    SessionInProgress,
};
typedef xyz::enum_value_t<Enum> Type;

} // namespace tool_state

namespace tool_other_instances_state
{

enum Enum
{
    NoOtherInstances,
    AnotherInstanceLaunchPending,
    AnotherInstanceRunning,
};
typedef xyz::enum_value_t<Enum> Type;

}

/// <summary>
/// Support tools session configuration
/// </summary>
/// @serializable
struct SessionConfig
{
    XYZ_DECLARE_SERID(0x4a0be718);  // crc32('product_platform.support_tools.SessionConfig')

    bool operator==(const SessionConfig& other) const noexcept
    {
        return errorScenario == other.errorScenario && 
            helpUrlTemplate == other.helpUrlTemplate &&
            recordingEnabled == other.recordingEnabled && 
            lowLevelTracesEnabled == other.lowLevelTracesEnabled &&
            useGdiForRecording == other.useGdiForRecording &&
            runGsiAfterFinish == other.runGsiAfterFinish;
    };
    
    error_scenario::Type errorScenario{ error_scenario::Other };
    xyz::string16_t helpUrlTemplate;
    
    xyz::bool_t recordingEnabled{true};
    xyz::bool_t lowLevelTracesEnabled{false};
    xyz::bool_t useGdiForRecording{false};
    xyz::bool_t runGsiAfterFinish{false};
};

/// <summary>
/// Tool startup info used in tool utility
/// </summary>
/// @serializable
struct StartupInfo
{
    XYZ_DECLARE_SERID(0xbe944e35);  // crc32('product_platform.support_tools.StartupInfo')

    xyz::string16_t onlineHelpUrl; // url for online help in tool utility
    tool_other_instances_state::Type otherInstancesState; // information about other running instances
    datetime_t sessionStartTime; // session start time in UTC 
                                 //(for continued recording after reboot will be earlier than current application launch)
};

}

}

#endif // PRODUCT_PLATFORM_IFACE_SUPPORT_TOOLS_TYPES_H
