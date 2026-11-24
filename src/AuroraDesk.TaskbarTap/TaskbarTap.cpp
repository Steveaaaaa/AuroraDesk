#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <ocidl.h>
#include <oleauto.h>
#include <xamlOM.h>
#include <wrl.h>
#include <wrl/implements.h>
#include <windows.ui.xaml.h>
#include <windows.ui.core.h>

#include <cstdint>
#include <cstring>
#include <mutex>
#include <string>
#include <unordered_map>
#include <unordered_set>
#include <vector>

using namespace Microsoft::WRL;
using namespace ABI::Windows::UI::Core;
using namespace ABI::Windows::UI::Xaml;

// {7A4E2F19-8C3B-4D6A-9E12-5B7F8C9D0E31}
static const CLSID CLSID_AuroraTaskbarTap = {
    0x7a4e2f19, 0x8c3b, 0x4d6a, { 0x9e, 0x12, 0x5b, 0x7f, 0x8c, 0x9d, 0x0e, 0x31 }
};

#pragma pack(push, 1)
struct TapCommand {
    uint32_t Magic;        // 0x41555241 ('AURA')
    uint32_t Cmd;          // 1 = SetOpacity, 2 = RestoreAndDetach
    float FillOpacity;     // 0.0f .. 1.0f
    float StrokeOpacity;   // 0.0f .. 1.0f
};
#pragma pack(pop)

enum class TargetKind {
    Fill,
    Stroke
};

struct TrackedElement {
    TargetKind Kind;
    ComPtr<IUIElement> Element;
};

class TaskbarTapSite
    : public RuntimeClass<
          RuntimeClassFlags<ClassicCom>,
          IObjectWithSite,
          IVisualTreeServiceCallback2> {
public:
    TaskbarTapSite() = default;

    ~TaskbarTapSite() {
        CleanupVisualTree();
    }

    // IObjectWithSite
    IFACEMETHODIMP SetSite(IUnknown* pUnkSite) override {
        if (!pUnkSite) {
            RestoreAndDetach();
            return S_OK;
        }

        ComPtr<IXamlDiagnostics> diag;
        HRESULT hr = pUnkSite->QueryInterface(IID_PPV_ARGS(&diag));
        if (FAILED(hr) || !diag) {
            return hr;
        }

        ComPtr<IVisualTreeService3> vts;
        hr = diag.As(&vts);
        if (FAILED(hr) || !vts) {
            return hr;
        }

        {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_site = pUnkSite;
            m_diag = diag;
            m_vts = vts;
        }

        BSTR initData = nullptr;
        std::wstring pipeName;
        if (SUCCEEDED(diag->GetInitializationData(&initData)) && initData) {
            pipeName.assign(initData, SysStringLen(initData));
            SysFreeString(initData);
        }

        if (!pipeName.empty()) {
            m_pipeName = pipeName;
            // Hold a strong reference for the worker thread
            ComPtr<TaskbarTapSite> self(this);
            auto* rawSelf = self.Detach();
            HANDLE hThread = CreateThread(
                nullptr, 0, &TaskbarTapSite::PipeThreadProc, rawSelf, 0, nullptr);
            if (hThread) {
                CloseHandle(hThread);
            } else {
                rawSelf->Release();
            }
        }

        return S_OK;
    }

    IFACEMETHODIMP GetSite(REFIID riid, void** ppvSite) override {
        if (!ppvSite) return E_POINTER;
        *ppvSite = nullptr;
        std::lock_guard<std::mutex> lock(m_mutex);
        if (!m_site) return E_FAIL;
        return m_site.CopyTo(riid, ppvSite);
    }

    // IVisualTreeServiceCallback2
    IFACEMETHODIMP OnElementStateChanged(
        InstanceHandle /*element*/,
        VisualElementState /*elementState*/,
        LPCWSTR /*context*/) override {
        return S_OK;
    }

    // IVisualTreeServiceCallback (runs on XAML UI thread)
    IFACEMETHODIMP OnVisualTreeChange(
        ParentChildRelation relation,
        VisualElement element,
        VisualMutationType mutationType) override {
        if (mutationType == Remove) {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_targets.erase(element.Handle);
            m_flyoutHandles.erase(element.Handle);
            return S_OK;
        }

        if (mutationType == Add) {
            const wchar_t* typeName = element.Type ? element.Type : L"";
            const wchar_t* elemName = element.Name ? element.Name : L"";

            // Track flyout subtrees so we do NOT make hover preview popups transparent
            bool isFlyoutNode = false;
            if (wcscmp(typeName, L"Taskbar.FlyoutFrame") == 0 ||
                wcscmp(elemName, L"HoverFlyoutBackgroundControl") == 0 ||
                wcscmp(elemName, L"HoverFlyoutBackground") == 0) {
                isFlyoutNode = true;
            } else {
                std::lock_guard<std::mutex> lock(m_mutex);
                if (relation.Parent != 0 && m_flyoutHandles.count(relation.Parent) > 0) {
                    isFlyoutNode = true;
                }
            }

            if (isFlyoutNode) {
                std::lock_guard<std::mutex> lock(m_mutex);
                m_flyoutHandles.insert(element.Handle);
                return S_OK;
            }

            if (elemName[0] != L'\0') {
                TargetKind kind;
                bool matched = false;
                if (wcscmp(elemName, L"BackgroundFill") == 0 ||
                    wcscmp(elemName, L"TaskbarFrameBackground") == 0) {
                    kind = TargetKind::Fill;
                    matched = true;
                } else if (wcscmp(elemName, L"BackgroundStroke") == 0 ||
                           wcscmp(elemName, L"ScreenEdgeStroke") == 0) {
                    kind = TargetKind::Stroke;
                    matched = true;
                }

                if (matched) {
                    ComPtr<IXamlDiagnostics> diag;
                    {
                        std::lock_guard<std::mutex> lock(m_mutex);
                        diag = m_diag;
                    }
                    if (diag) {
                        ComPtr<IInspectable> insp;
                        if (SUCCEEDED(diag->GetIInspectableFromHandle(element.Handle, &insp)) && insp) {
                            ComPtr<IUIElement> uiElem;
                            if (SUCCEEDED(insp.As(&uiElem)) && uiElem) {
                                ComPtr<IDependencyObject> depObj;
                                ComPtr<ICoreDispatcher> disp;
                                if (SUCCEEDED(insp.As(&depObj)) && depObj) {
                                    depObj->get_Dispatcher(&disp);
                                }

                                double targetOpacity = 1.0;
                                {
                                    std::lock_guard<std::mutex> lock(m_mutex);
                                    if (!m_dispatcher && disp) {
                                        m_dispatcher = disp;
                                    }
                                    m_targets[element.Handle] = TrackedElement{ kind, uiElem };
                                    targetOpacity = (kind == TargetKind::Fill)
                                        ? static_cast<double>(m_fillOpacity)
                                        : static_cast<double>(m_strokeOpacity);
                                }

                                // Already on the XAML UI thread -- apply immediately
                                uiElem->put_Opacity(targetOpacity);
                            }
                        }
                    }
                }
            }
        }

        return S_OK;
    }

private:
    void ApplyOpacityOnUiThread() {
        std::vector<std::pair<ComPtr<IUIElement>, double>> snapshot;
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            snapshot.reserve(m_targets.size());
            for (const auto& kv : m_targets) {
                double op = (kv.second.Kind == TargetKind::Fill)
                    ? static_cast<double>(m_fillOpacity)
                    : static_cast<double>(m_strokeOpacity);
                snapshot.emplace_back(kv.second.Element, op);
            }
        }
        for (auto& item : snapshot) {
            if (item.first) {
                item.first->put_Opacity(item.second);
            }
        }
    }

    void DispatchApplyOpacity(bool waitForCompletion) {
        ComPtr<ICoreDispatcher> disp;
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            disp = m_dispatcher;
        }
        if (!disp) return;

        HANDLE hDone = waitForCompletion ? CreateEventW(nullptr, TRUE, FALSE, nullptr) : nullptr;
        ComPtr<TaskbarTapSite> self(this);

        auto handler = Callback<Implements<RuntimeClassFlags<ClassicCom>, IDispatchedHandler, FtmBase>>(
            [self, hDone]() -> HRESULT {
                self->ApplyOpacityOnUiThread();
                if (hDone) SetEvent(hDone);
                return S_OK;
            });

        ComPtr<ABI::Windows::Foundation::IAsyncAction> asyncAction;
        HRESULT hr = disp->RunAsync(CoreDispatcherPriority_Normal, handler.Get(), &asyncAction);
        if (SUCCEEDED(hr) && hDone) {
            WaitForSingleObject(hDone, 500);
        }
        if (hDone) CloseHandle(hDone);
    }

    void CleanupVisualTree() {
        ComPtr<IVisualTreeService3> vts;
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            vts = m_vts;
            m_vts.Reset();
            m_diag.Reset();
            m_site.Reset();
            m_targets.clear();
            m_flyoutHandles.clear();
            m_dispatcher.Reset();
        }
        if (vts) {
            vts->UnadviseVisualTreeChange(this);
        }
    }

    void RestoreAndDetach() {
        {
            std::lock_guard<std::mutex> lock(m_mutex);
            m_fillOpacity = 1.0f;
            m_strokeOpacity = 1.0f;
        }
        DispatchApplyOpacity(true);
        CleanupVisualTree();
    }

    static DWORD WINAPI PipeThreadProc(LPVOID param) {
        ComPtr<TaskbarTapSite> self;
        self.Attach(static_cast<TaskbarTapSite*>(param));

        CoInitializeEx(nullptr, COINIT_MULTITHREADED);

        // Subscribe to the visual tree from the worker thread after SetSite has returned
        {
            ComPtr<IVisualTreeService3> vts;
            {
                std::lock_guard<std::mutex> lock(self->m_mutex);
                vts = self->m_vts;
            }
            if (vts) {
                vts->AdviseVisualTreeChange(self.Get());
            }
        }

        HANDLE hPipe = INVALID_HANDLE_VALUE;
        for (int attempt = 0; attempt < 30; ++attempt) {
            hPipe = CreateFileW(
                self->m_pipeName.c_str(),
                GENERIC_READ,
                0,
                nullptr,
                OPEN_EXISTING,
                0,
                nullptr);
            if (hPipe != INVALID_HANDLE_VALUE) break;
            Sleep(50);
        }

        if (hPipe != INVALID_HANDLE_VALUE) {
            TapCommand cmd{};
            DWORD bytesRead = 0;
            while (ReadFile(hPipe, &cmd, sizeof(cmd), &bytesRead, nullptr) && bytesRead == sizeof(cmd)) {
                if (cmd.Magic != 0x41555241u) continue;
                if (cmd.Cmd == 1) {
                    float fill = cmd.FillOpacity < 0.0f ? 0.0f : (cmd.FillOpacity > 1.0f ? 1.0f : cmd.FillOpacity);
                    float stroke = cmd.StrokeOpacity < 0.0f ? 0.0f : (cmd.StrokeOpacity > 1.0f ? 1.0f : cmd.StrokeOpacity);
                    {
                        std::lock_guard<std::mutex> lock(self->m_mutex);
                        self->m_fillOpacity = fill;
                        self->m_strokeOpacity = stroke;
                    }
                    self->DispatchApplyOpacity(false);
                } else if (cmd.Cmd == 2) {
                    break;
                }
            }
            CloseHandle(hPipe);
        }

        // Always restore native taskbar appearance when pipe disconnects or detaches
        self->RestoreAndDetach();

        CoUninitialize();
        return 0;
    }

    std::mutex m_mutex;
    ComPtr<IUnknown> m_site;
    ComPtr<IXamlDiagnostics> m_diag;
    ComPtr<IVisualTreeService3> m_vts;
    ComPtr<ICoreDispatcher> m_dispatcher;
    std::unordered_map<InstanceHandle, TrackedElement> m_targets;
    std::unordered_set<InstanceHandle> m_flyoutHandles;
    std::wstring m_pipeName;
    float m_fillOpacity = 0.0f;
    float m_strokeOpacity = 0.0f;
};

STDAPI DllGetClassObject(REFCLSID rclsid, REFIID riid, LPVOID* ppv) {
    if (!ppv) return E_POINTER;
    *ppv = nullptr;
    if (rclsid == CLSID_AuroraTaskbarTap) {
        ComPtr<SimpleClassFactory<TaskbarTapSite>> factory;
        HRESULT hr = MakeAndInitialize<SimpleClassFactory<TaskbarTapSite>>(&factory);
        if (FAILED(hr)) return hr;
        return factory.CopyTo(riid, ppv);
    }
    return CLASS_E_CLASSNOTAVAILABLE;
}

STDAPI DllCanUnloadNow() {
    return S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID /*lpReserved*/) {
    if (ul_reason_for_call == DLL_PROCESS_ATTACH) {
        DisableThreadLibraryCalls(hModule);
    }
    return TRUE;
}
