
#define LOADER_TRACE_PREFIX "Loader\t"
#define LOADER_TRACE_ERROR() XYZ_TRACE_ERROR_EX(GetTracer()) << LOADER_TRACE_PREFIX
#define LOADER_TRACE_INFO() XYZ_TRACE_INFO_EX(GetTracer()) << LOADER_TRACE_PREFIX

#include "native_tracer.h"
// should be upper than execution_environment_checker_wrapper to enable XYZ_PLATFORM_WINDOWS
#include <component/xyz/config/platform.h>

#include <common/execution_environment_checker_wrapper.h>

#include <platform/ui/core_clr/hostfxr_module.h>
#include <platform/product_info/product_info.h>
#include <platform/util/reg_utils.h>
#include <platform/util/windows/sys_info.h>
#include <platform/util/windows/filesystem.h>
#include <platform/util/product_environment_helpers.h>
#include <platform/core/env_vars.h>

#include <component/xyz/system/file/file.h>
#include <component/xyz/trace/tracer_factory.h>
#include <component/xyz/datetime/format.h>
#include <component/xyz/iface/trace.h>
#include <component/xyz/service/tracer/tracer_channel.h>
#include <component/xyz/util/helpers/scope_guard.h>
#include <component/xyz/rtl/conversion/text_conv.h>
#include <component/xyz/rtl/enum_value.h>
#include <component/xyz/system/process/windows/this_process.h>

#include <windows.h>
#include <comdef.h>
#include <atlbase.h>
#include <atlcom.h>
#include <objbase.h>
#include <comip.h>
#include <map>

xyz::objptr_t<xyz::ITracer> g_tracer;
ATL::CComModule g_atlModule;

namespace pe = product_security::execution_environment_checker3;

#ifndef PRODUCT_PLATFORM_PRODUCT64
#define PeFlags (pe::check_flags::SetSecureEnvironmentPaths | pe::check_flags::CheckExecutablePath | pe::check_flags::CheckExecutableName)
#else
#define PeFlags (pe::check_flags::SetSecureEnvironmentPaths | pe::check_flags::CheckExecutablePath | pe::check_flags::CheckExecutableName | pe::check_flags::UseNativeRegistryPath)
#endif

EEC_SETUP_STATIC_DESCRIPTOR_WRAPPER(PeFlags, L"troubleshoot.exe");

xyz::ITracer* GetTracer(void)
{
	return g_tracer;
}

namespace
{

constexpr char16_t DotNetValueName[] = u"DotnetCurrentPath";
constexpr wchar_t TracePathOption[] = L"--trace-path";
constexpr wchar_t TraceLevelOption[] = L"--trace-level";
constexpr wchar_t ExecutableName[] = L"troubleshoot.exe";

namespace tool_exit_codes
{

enum Enum
{
	Regular = 0,
	Error = 2,
	WindowsShutdown = 3,
	ForceShutdown = 4,
	AnotherInstanceRunning = 5,
};
using Type = xyz::enum_value_t<Enum>;

};

xyz::string16_t GetDotnetPath()
{
	const auto productInfo = product_info::GetProductInfo();
	const auto regDataPath = productInfo.GetProductDataRegistryPath();

	product_platform::RegKey regKey;
	XYZ_CHECK_SUCCEEDED_RETURN_EX(regKey.Open(HKEY_LOCAL_MACHINE, regDataPath.c_str(), KEY_WOW64_32KEY), xyz::string16_t());

	xyz::string16_t dotNetPath;
	XYZ_CHECK_SUCCEEDED_RETURN_EX (regKey.QueryValue(DotNetValueName, dotNetPath), xyz::string16_t());

	return dotNetPath;
}

bool ShouldDeleteTraceOnExit()
{
	const auto productInfo = product_info::GetProductInfo();
	return productInfo.GetEnvironmentString<string16_t>(XYZ_TEXT(ENV_SUPPORT_TOOLS_DISABLE_CLEARING_TRACES)) != u"1";
}

void AppendTracesPathToArgs(const xyz::string16_t& fullTracePath, xyz::vector_t<CComBSTR>& result)
{
	result.emplace_back(CComBSTR(TracePathOption));

	const auto tracePathW = xyz::text::Cast<std::wstring>(fullTracePath);
	result.emplace_back(CComBSTR(tracePathW.c_str()));
}

xyz::vector_t<std::wstring> GetCmdLineArguments(LPWSTR lpCmdLine)
{
	int argc = 0;
	wchar_t** argv = ::CommandLineToArgvW(lpCmdLine, &argc);

	xyz::vector_t<std::wstring> args;
	
	if (argv && argc > 0)
	{
		args.reserve(argc);

		if (!::wcsstr(argv[0], ExecutableName))
		{
			args.emplace_back(argv[0]);
		}

		for (int i = 1; i < argc; ++i)
			args.emplace_back(argv[i]);
	}
	::LocalFree(argv);
	
	return args;
}

void AppendCmdlineArguments(LPWSTR lpCmdLine, xyz::vector_t<CComBSTR>& result)
{
	int argc = 0;
	wchar_t** argv = ::CommandLineToArgvW(lpCmdLine, &argc);

	if (argv && argc > 0)
	{
		result.reserve(result.size() + argc);

		if (!::wcsstr(argv[0], ExecutableName))
		{
			result.emplace_back(CComBSTR(argv[0]));
		}

		for (int i = 1; i < argc; ++i)
			result.emplace_back(CComBSTR(argv[i]));
	}
	::LocalFree(argv);
}

std::wstring GetArgumentValue(const xyz::vector_t<std::wstring>& args, const wchar_t *argName)
{
	auto it = std::find_if(args.cbegin(), args.cend(), [argName] (const std::wstring& arg)
	{
		return arg._Starts_with(argName);
	});
	if (it == args.cend())
	{
		return {};
	}
	const auto& argument = *it;
	const auto separatorPos = argument.find_first_of(L'=', ::wcslen(argName));

	if (separatorPos == std::wstring::npos)
	{
		return {};
	}
	return argument.substr(separatorPos + 1U);
}

void FillBstrArguments(const xyz::vector_t<std::wstring>& args, xyz::vector_t<CComBSTR>& bstrArgs)
{
	bstrArgs.reserve(bstrArgs.size() + args.size());

	for (const auto& arg : args)
	{
		bstrArgs.emplace_back(CComBSTR(arg.c_str()));
	}
}

xyz::ITracer::TraceLevel TraceLevelFromString(const std::wstring& traceLevelStr)
{
	const std::map<std::wstring, xyz::ITracer::TraceLevel> traceLevelToString
	{
		{L"NON", xyz::ITracer::tlNON  },
		{L"ALW", xyz::ITracer::tlALW  },
		{L"CRT", xyz::ITracer::tlCRT  },
		{L"ERR", xyz::ITracer::tlERR  },
		{L"WARN", xyz::ITracer::tlWRN },
		{L"IMP", xyz::ITracer::tlIMP  },
		{L"INF", xyz::ITracer::tlINF  },
		{L"DBG", xyz::ITracer::tlDBG  },
	};
	const auto it = traceLevelToString.find(traceLevelStr);
	return (it != traceLevelToString.cend()) ? it->second : xyz::ITracer::tlINF;
}

using namespace ATL;

class ATL_NO_VTABLE NativeTracerImpl 
	: public CComObjectRootEx<CComMultiThreadModel>
	, public CComCoClass<NativeTracerImpl>
	, public INativeTracer
{
public:
	BEGIN_COM_MAP(NativeTracerImpl)
		COM_INTERFACE_ENTRY(INativeTracer)
		COM_INTERFACE_ENTRY_AGGREGATE(IID_IMarshal, m_pUnkMarshaler.p)
	END_COM_MAP()

	DECLARE_GET_CONTROLLING_UNKNOWN()

	NativeTracerImpl()
		: m_pUnkMarshaler(nullptr)
	{
	}

	~NativeTracerImpl()
	{
		m_pUnkMarshaler = nullptr;
	}

	HRESULT FinalConstruct()
	{
		return ::CoCreateFreeThreadedMarshaler(GetControllingUnknown(), &m_pUnkMarshaler.p);
	}

	void FinalRelease() 
	{ 
		m_pUnkMarshaler.Release(); 
	}

	STDMETHODIMP_(uint32_t) GetMaxTraceLevel ()
	{
		return static_cast<uint32_t>(xyz::ITracer::tlDBG);
	}

	STDMETHODIMP_(void) Trace (uint32_t level, const wchar_t *message)
	{
		if (!m_tracer->ShouldTrace(level))
		{
			return;
		}

		xyz::string8_t utf8Message;
		const result_t rc = xyz::text::ConvertEx<xyz::text::WideCharConverter, xyz::text::Utf8CharConverter>(message, utf8Message);
		XYZ_CHECK_SUCCEEDED_RETURN_EX(rc, void());

		const size_t size = utf8Message.length();
		str8_t msg = nullptr;

		if (XYZ_SUCCEEDED(m_tracer->PrepareMsg(level, &msg, size)))
		{
			::memcpy_s(msg, size, utf8Message.c_str(), size);
			XYZ_IGNORE_RESULT(m_tracer->TraceMsg(msg, size));
		}
	}

	STDMETHODIMP_(void) Flush ()
	{
		XYZ_IGNORE_RESULT(m_flusher->Flush());
	}

	void Init (xyz::objptr_t<xyz::ITracer> tracer, xyz::objptr_t<xyz::tracer::IChannelFlusher> flusher)
	{
		m_tracer = std::move(tracer);
		m_flusher = std::move(flusher);
	}

	void Deinit()
	{
		m_tracer.reset();
		m_flusher.reset();
	}

private:
	xyz::objptr_t<xyz::ITracer> m_tracer;
	xyz::objptr_t<xyz::tracer::IChannelFlusher> m_flusher;
	CComPtr<IUnknown> m_pUnkMarshaler;
};

CComObject<NativeTracerImpl>* CreateNativeTracer(xyz::objptr_t<xyz::ITracer> tracer, xyz::objptr_t<xyz::tracer::IChannelFlusher> flusher)
{
	CComObject<NativeTracerImpl>* nativeTracer;
	HRESULT hr = CComObject<NativeTracerImpl>::CreateInstance(&nativeTracer);
	if (FAILED(hr))
	{
		LOADER_TRACE_ERROR() << "Failed to create native tracer object, error - " << std::hex << hr;
		return nullptr;
	}

	nativeTracer->Init(std::move(tracer), std::move(flusher));

	return nativeTracer;
}

xyz::string16_t GetFullTracePath(const xyz::string16_t& traceRoot)
{
	const auto currentTime = xyz::DateTime::Current();
	const auto pid = static_cast<uint32_t> (xyz::windows::this_process::GetID());

	xyz::string16_t traceFileName;

	using FilenameFormat = XYZ_MPL_STRING("%dd.%mm.%YY_%HH.%MM.%SS");
	xyz::stream::stream(traceFileName) << u"troubleshoot_" << pid << "_"
		<< xyz::format_datetime<FilenameFormat>(currentTime) << ".log";

	return xyz::filesystem::PathConcatenate(traceRoot, traceFileName);
}

void FixDefaultDllDirectories()
{
	if (product_platform::windows::IsWindows8OrLater())
	{
		product_platform::windows::SetDefaultDllDirectories(
			LOAD_LIBRARY_SEARCH_APPLICATION_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
	}
	else
	{
		SetEnvironmentVariableW(L"PATH", L"");
	}
	::SetDllDirectoryW(L"");
}

}

int CALLBACK wWinMain(HINSTANCE hInstance,
					 HINSTANCE hPrevInstance,
					 LPWSTR    lpCmdLine,
					 int       nCmdShow)
{
	FixDefaultDllDirectories();
	
	const auto productInfo = product_info::GetProductInfo();

	const auto productRoot = productInfo.GetEnvironmentString(ENV_PRODUCTROOT);
	const auto traceRoot = productInfo.GetEnvironmentString(ENV_TRACEROOT);
	const auto fullTracePath = GetFullTracePath(traceRoot);
	
	int managedExitCode = 0;
	{
		::CoInitializeEx(nullptr, COINIT_MULTITHREADED);

		XYZ_SCOPE_GUARD(xyz::make_guard([&]() 
		{ 
			::CoUninitialize();	
		}));
		
		xyz::string16_t traceFilePath(fullTracePath);
		const auto args = GetCmdLineArguments(lpCmdLine);
		const auto traceLevelStr = GetArgumentValue(args, TraceLevelOption);
		auto traceLevel = TraceLevelFromString(traceLevelStr);

		xyz::tracer::FileChannelConfiguration config (traceLevel, std::move(traceFilePath));
		auto fileChannel = xyz::trace::CreateFileChannel(std::move(config));
		const auto flusher = xyz::query_interface_cast<xyz::tracer::IChannelFlusher>(fileChannel);

		g_tracer = xyz::trace::CreateTracer(fileChannel);

		LOADER_TRACE_INFO() << "Launching troubleshoot with commandline " << lpCmdLine;

		xyz::string16_t dotNetPath = GetDotnetPath();
		LOADER_TRACE_INFO() << "Dotnet path is " << dotNetPath;

		auto nativeTracer = CreateNativeTracer(g_tracer, std::move(flusher));
		if (!nativeTracer)
		{
			return 1;
		}
		IUnknownPtr tracerPtr(nativeTracer);

		xyz::vector_t<CComBSTR> bstrArgs;
		AppendTracesPathToArgs(fullTracePath, bstrArgs);
		FillBstrArguments(args, bstrArgs);

		xyz::vector_t<const void*> rawArgs { nativeTracer->GetUnknown() };
		std::transform(bstrArgs.cbegin(), bstrArgs.cend(), std::back_inserter(rawArgs), [](const CComBSTR& arg)
		{
			return static_cast<BSTR> (arg);
		});

		product_platform::ui::core_clr::HostFxrModuleParameters params{
			u"",
			productRoot,
			u"8.0.",
			L"troubleshoot.runtimeconfig.json",
			L"troubleshoot.dll",
			L"KasperskyLab.UI.Troubleshooting.Program, troubleshoot",
			L"UnmanagedEntryPoint"
		};

		product_platform::ui::core_clr::HostFxrModule hostModule(dotNetPath, params);
		LOADER_TRACE_INFO() << "Host module created";

		component_entry_point_fn entryPoint;
		if (!hostModule.GetStartupFunction(entryPoint))
		{
			LOADER_TRACE_ERROR() << "Failed to get tool managed entry point";
			return 1;
		}

		LOADER_TRACE_INFO() << "Calling managed entry point";
		managedExitCode = entryPoint(&rawArgs.front(), static_cast<int32_t>(rawArgs.size()));

		LOADER_TRACE_INFO() << "Managed entry point exited with code " << managedExitCode;
		
		//Explicit deinit is required to ensure all references to fileChannel will be freed
		nativeTracer->Deinit();
	}
	g_tracer.reset();

	const auto exitCode = static_cast<tool_exit_codes::Enum>(managedExitCode);
	if (exitCode == tool_exit_codes::Regular && xyz::sOk == xyz::filesystem::IsExists(fullTracePath) && ShouldDeleteTraceOnExit())
	{
		XYZ_IGNORE_RESULT(xyz::filesystem::RemoveFile(fullTracePath));
	}
	return managedExitCode;
}
