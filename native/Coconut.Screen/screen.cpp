// Coconut: Windows Graphics Capture -> D3D11/NV12 -> MF H.264, and MF -> SwapChainPanel.
// GPL-3.0-or-later. No frame is written to disk; callbacks borrow bytes only for their duration.
#include <windows.h>
#include <d3d11_4.h>
#include <dxgi1_3.h>
#include <mfapi.h>
#include <mfidl.h>
#include <mftransform.h>
#include <mferror.h>
#include <codecapi.h>
#include <wmcodecdsp.h>
#include <windows.graphics.capture.interop.h>
#include <windows.graphics.directx.direct3d11.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Graphics.Capture.h>
#include <winrt/Windows.Graphics.DirectX.Direct3D11.h>
#include <atomic>
#include <chrono>
#include <condition_variable>
#include <functional>
#include <future>
#include <mutex>
#include <thread>
#include <vector>
#include <algorithm>

using namespace winrt;
using namespace winrt::Windows::Graphics::Capture;
using namespace winrt::Windows::Graphics::DirectX;
using namespace winrt::Windows::Graphics::DirectX::Direct3D11;
using namespace std::chrono;
using FrameCallback = void(__cdecl*)(void*, const uint8_t*, int, int64_t, int, const uint8_t*, int);
using ErrorCallback = void(__cdecl*)(void*, int);
#define API extern "C" __declspec(dllexport)
struct Cleanup {
    std::function<void()> action;
    ~Cleanup() { try { action(); } catch (...) { } }
};

static void init_mf() {
    struct Apartment {
        HRESULT hr=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
        Apartment() { if (FAILED(hr) && hr!=RPC_E_CHANGED_MODE) check_hresult(hr); }
        ~Apartment() { if (SUCCEEDED(hr)) CoUninitialize(); }
    };
    static thread_local Apartment apartment;
    static std::once_flag once;
    std::call_once(once, [] { check_hresult(MFStartup(MF_VERSION, MFSTARTUP_FULL)); });
}
static com_ptr<IMFSample> sample_with_buffer(DWORD size) {
    com_ptr<IMFSample> sample; check_hresult(MFCreateSample(sample.put()));
    com_ptr<IMFMediaBuffer> buffer; check_hresult(MFCreateMemoryBuffer(size, buffer.put()));
    check_hresult(sample->AddBuffer(buffer.get())); return sample;
}
static com_ptr<IMFMediaType> video_type(GUID subtype, int w, int h, int fps) {
    com_ptr<IMFMediaType> type; check_hresult(MFCreateMediaType(type.put()));
    check_hresult(type->SetGUID(MF_MT_MAJOR_TYPE, MFMediaType_Video));
    check_hresult(type->SetGUID(MF_MT_SUBTYPE, subtype));
    check_hresult(type->SetUINT32(MF_MT_INTERLACE_MODE, MFVideoInterlace_Progressive));
    check_hresult(MFSetAttributeSize(type.get(), MF_MT_FRAME_SIZE, w, h));
    check_hresult(MFSetAttributeRatio(type.get(), MF_MT_FRAME_RATE, fps, 1));
    check_hresult(MFSetAttributeRatio(type.get(), MF_MT_PIXEL_ASPECT_RATIO, 1, 1));
    return type;
}
struct Device {
    com_ptr<ID3D11Device> device;
    com_ptr<ID3D11DeviceContext> context;
    com_ptr<IMFDXGIDeviceManager> manager;
    Device() {
        UINT flags = D3D11_CREATE_DEVICE_BGRA_SUPPORT | D3D11_CREATE_DEVICE_VIDEO_SUPPORT;
        check_hresult(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, flags, nullptr, 0,
            D3D11_SDK_VERSION, device.put(), nullptr, context.put()));
        auto mt = context.as<ID3D11Multithread>(); mt->SetMultithreadProtected(TRUE);
        UINT token; check_hresult(MFCreateDXGIDeviceManager(&token, manager.put()));
        check_hresult(manager->ResetDevice(device.get(), token));
    }
    com_ptr<ID3D11Texture2D> texture(int w, int h, DXGI_FORMAT format, UINT bind,
        D3D11_USAGE usage = D3D11_USAGE_DEFAULT, UINT cpu = 0) {
        D3D11_TEXTURE2D_DESC d{}; d.Width=w; d.Height=h; d.Format=format;
        d.ArraySize=1; d.MipLevels=1; d.SampleDesc.Count=1; d.BindFlags=bind; d.Usage=usage; d.CPUAccessFlags=cpu;
        com_ptr<ID3D11Texture2D> result; check_hresult(device->CreateTexture2D(&d, nullptr, result.put())); return result;
    }
};
struct Processor {
    Device& d;
    com_ptr<ID3D11VideoDevice> video;
    com_ptr<ID3D11VideoContext> context;
    com_ptr<ID3D11VideoProcessorEnumerator> enumerator;
    com_ptr<ID3D11VideoProcessor> processor;
    int width, height;
    Processor(Device& device, int iw, int ih, int ow, int oh) : d(device), width(ow), height(oh) {
        video=d.device.as<ID3D11VideoDevice>(); context=d.context.as<ID3D11VideoContext>();
        D3D11_VIDEO_PROCESSOR_CONTENT_DESC desc{}; desc.InputFrameFormat=D3D11_VIDEO_FRAME_FORMAT_PROGRESSIVE;
        desc.InputWidth=iw; desc.InputHeight=ih; desc.OutputWidth=ow; desc.OutputHeight=oh;
        desc.Usage=D3D11_VIDEO_USAGE_PLAYBACK_NORMAL;
        check_hresult(video->CreateVideoProcessorEnumerator(&desc, enumerator.put()));
        check_hresult(video->CreateVideoProcessor(enumerator.get(), 0, processor.put()));
        RECT src{0,0,iw,ih}, dst{0,0,ow,oh};
        context->VideoProcessorSetStreamSourceRect(processor.get(), 0, TRUE, &src);
        context->VideoProcessorSetStreamDestRect(processor.get(), 0, TRUE, &dst);
        context->VideoProcessorSetOutputTargetRect(processor.get(), TRUE, &dst);
        context->VideoProcessorSetStreamAutoProcessingMode(processor.get(), 0, FALSE);
    }
    void convert(ID3D11Texture2D* input, ID3D11Texture2D* output, UINT slice=0) {
        D3D11_VIDEO_PROCESSOR_INPUT_VIEW_DESC in{}; in.ViewDimension=D3D11_VPIV_DIMENSION_TEXTURE2D;
        in.Texture2D.ArraySlice=slice;
        com_ptr<ID3D11VideoProcessorInputView> iv;
        check_hresult(video->CreateVideoProcessorInputView(input, enumerator.get(), &in, iv.put()));
        D3D11_VIDEO_PROCESSOR_OUTPUT_VIEW_DESC out{}; out.ViewDimension=D3D11_VPOV_DIMENSION_TEXTURE2D;
        com_ptr<ID3D11VideoProcessorOutputView> ov;
        check_hresult(video->CreateVideoProcessorOutputView(output, enumerator.get(), &out, ov.put()));
        D3D11_VIDEO_PROCESSOR_STREAM stream{}; stream.Enable=TRUE; stream.pInputSurface=iv.get();
        check_hresult(context->VideoProcessorBlt(processor.get(), ov.get(), 0, 1, &stream));
    }
};
struct Encoder {
    Device& device;
    com_ptr<IMFTransform> transform;
    com_ptr<IMFMediaEventGenerator> events;
    com_ptr<ICodecAPI> codec;
    bool hardware=false, gpu=false;
    int needsInput=0, width, height, fps;
    std::function<void(IMFSample*, const std::vector<uint8_t>&)> output;
    std::vector<uint8_t> sequenceHeader;
    Encoder(Device& d, int w, int h, int f, int bitrate, bool software,
        std::function<void(IMFSample*, const std::vector<uint8_t>&)> cb) : device(d), width(w), height(h), fps(f), output(std::move(cb)) {
        if (!software) {
            MFT_REGISTER_TYPE_INFO input{MFMediaType_Video, MFVideoFormat_NV12}, out{MFMediaType_Video, MFVideoFormat_H264};
            IMFActivate** activations=nullptr; UINT count=0;
            if (SUCCEEDED(MFTEnumEx(MFT_CATEGORY_VIDEO_ENCODER, MFT_ENUM_FLAG_HARDWARE | MFT_ENUM_FLAG_SORTANDFILTER,
                &input, &out, &activations, &count))) {
                for (UINT i=0; i<count; ++i) {
                    if (!transform) {
                        try { check_hresult(activations[i]->ActivateObject(__uuidof(IMFTransform), transform.put_void()));
                            configure(bitrate); hardware=true;
                        } catch (...) { transform=nullptr; codec=nullptr; events=nullptr; gpu=false; }
                    }
                    activations[i]->Release();
                }
                CoTaskMemFree(activations);
            }
        }
        if (!transform) {
            check_hresult(CoCreateInstance(CLSID_CMSH264EncoderMFT, nullptr, CLSCTX_INPROC_SERVER,
                __uuidof(IMFTransform), transform.put_void())); configure(bitrate);
        }
        check_hresult(transform->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING, 0));
        check_hresult(transform->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM, 0));
    }
    ~Encoder() { if (transform) { transform->ProcessMessage(MFT_MESSAGE_NOTIFY_END_OF_STREAM, 0);
        transform->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH, 0); if (auto shutdown=transform.try_as<IMFShutdown>()) shutdown->Shutdown(); } }
    HRESULT setting(GUID key, ULONG value) {
        if (!codec) return E_NOINTERFACE;
        VARIANT v{}; v.vt=VT_UI4; v.ulVal=value; return codec->SetValue(&key, &v);
    }
    void boolean(GUID key, bool value) {
        if (!codec) return;
        VARIANT v{}; v.vt=VT_BOOL; v.boolVal=value ? VARIANT_TRUE : VARIANT_FALSE; codec->SetValue(&key,&v);
    }
    void emit(IMFSample* sample) {
        if (sequenceHeader.empty()) {
            com_ptr<IMFMediaType> type;
            if (SUCCEEDED(transform->GetOutputCurrentType(0,type.put()))) {
                UINT32 size=0;
                if (SUCCEEDED(type->GetBlobSize(MF_MT_MPEG_SEQUENCE_HEADER,&size)) && size>0 && size<=65536) {
                    sequenceHeader.resize(size);
                    if (FAILED(type->GetBlob(MF_MT_MPEG_SEQUENCE_HEADER,sequenceHeader.data(),size,nullptr))) sequenceHeader.clear();
                }
            }
        }
        output(sample,sequenceHeader);
    }
    void configure(int bitrate) {
        com_ptr<IMFAttributes> attrs; check_hresult(transform->GetAttributes(attrs.put()));
        if (MFGetAttributeUINT32(attrs.get(), MF_TRANSFORM_ASYNC, FALSE)) {
            check_hresult(attrs->SetUINT32(MF_TRANSFORM_ASYNC_UNLOCK, TRUE)); events=transform.as<IMFMediaEventGenerator>();
        }
        attrs->SetUINT32(MF_LOW_LATENCY, TRUE);
        if (MFGetAttributeUINT32(attrs.get(), MF_SA_D3D11_AWARE, FALSE)) {
            gpu=SUCCEEDED(transform->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER, reinterpret_cast<ULONG_PTR>(device.manager.get())));
        }
        codec=transform.try_as<ICodecAPI>();
        setting(CODECAPI_AVEncCommonRateControlMode, eAVEncCommonRateControlMode_CBR);
        setting(CODECAPI_AVEncCommonMeanBitRate, bitrate);
        setting(CODECAPI_AVEncMPVDefaultBPictureCount, 0);
        setting(CODECAPI_AVEncMPVGOPSize, fps);
        boolean(CODECAPI_AVEncCommonLowLatency, true);
        boolean(CODECAPI_AVEncCommonRealTime, true);
        auto out=video_type(MFVideoFormat_H264, width, height, fps);
        check_hresult(out->SetUINT32(MF_MT_AVG_BITRATE, bitrate));
        check_hresult(out->SetUINT32(MF_MT_MPEG2_PROFILE, eAVEncH264VProfile_Base));
        check_hresult(transform->SetOutputType(0, out.get(), 0));
        auto in=video_type(MFVideoFormat_NV12, width, height, fps);
        check_hresult(transform->SetInputType(0, in.get(), 0));
    }
    void drain_one() {
        MFT_OUTPUT_STREAM_INFO info{}; check_hresult(transform->GetOutputStreamInfo(0, &info));
        com_ptr<IMFSample> allocated;
        if (!(info.dwFlags & MFT_OUTPUT_STREAM_PROVIDES_SAMPLES)) allocated=sample_with_buffer(std::max(info.cbSize, DWORD(width*height*2)));
        MFT_OUTPUT_DATA_BUFFER buffer{}; buffer.pSample=allocated.get(); DWORD status=0;
        HRESULT hr=transform->ProcessOutput(0, 1, &buffer, &status);
        if (buffer.pEvents) buffer.pEvents->Release();
        com_ptr<IMFSample> produced;
        if (buffer.pSample && buffer.pSample!=allocated.get()) produced.attach(buffer.pSample);
        if (hr==MF_E_TRANSFORM_NEED_MORE_INPUT) return;
        check_hresult(hr);
        if (buffer.pSample) emit(buffer.pSample);
    }
    void pump() {
        if (!events) return;
        for (;;) {
            com_ptr<IMFMediaEvent> event;
            HRESULT hr=events->GetEvent(MF_EVENT_FLAG_NO_WAIT, event.put());
            if (hr==MF_E_NO_EVENTS_AVAILABLE) break;
            check_hresult(hr); HRESULT status; check_hresult(event->GetStatus(&status)); check_hresult(status);
            MediaEventType type; check_hresult(event->GetType(&type));
            if (type==METransformNeedInput) ++needsInput;
            if (type==METransformHaveOutput) drain_one();
        }
    }
    bool push(IMFSample* sample) {
        pump(); if (events && needsInput<=0) return false;
        HRESULT hr=transform->ProcessInput(0, sample, 0);
        if (hr==MF_E_NOTACCEPTING) return false;
        check_hresult(hr); if (events) { --needsInput; pump(); }
        else { for (;;) {
            MFT_OUTPUT_STREAM_INFO info{}; check_hresult(transform->GetOutputStreamInfo(0, &info));
            auto result=sample_with_buffer(std::max(info.cbSize, DWORD(width*height*2)));
            MFT_OUTPUT_DATA_BUFFER buffer{}; buffer.pSample=result.get(); DWORD status=0;
            hr=transform->ProcessOutput(0, 1, &buffer, &status);
            if (buffer.pEvents) buffer.pEvents->Release();
            if (hr==MF_E_TRANSFORM_NEED_MORE_INPUT) break;
            check_hresult(hr); emit(result.get());
        } }
        return true;
    }
};

struct Capture {
    struct Frames {
        std::mutex mutex;
        std::condition_variable ready;
        Direct3D11CaptureFrame latest{nullptr};
        std::atomic<bool> closed=false;
        bool stopped=false;
        ~Frames() { if (latest) latest.Close(); }
    };
    std::jthread thread;
    std::atomic<int> bitrate, keyframe{0}, hardware{0};
    std::atomic<int> reconfigure{0};
    std::shared_ptr<Frames> frames=std::make_shared<Frames>();
    Capture(HMONITOR monitor, int w, int h, int fps, int rate, bool software, FrameCallback cb, ErrorCallback error, void* user)
        : bitrate(rate) {
        std::promise<HRESULT> started; auto result=started.get_future();
        thread=std::jthread([=,this,promise=std::move(started)](std::stop_token stop) mutable {
            bool notified=false;
            try {
                init_apartment(apartment_type::multi_threaded); init_mf(); Device device;
                auto itemFactory=get_activation_factory<GraphicsCaptureItem, IGraphicsCaptureItemInterop>();
                GraphicsCaptureItem item{nullptr};
                check_hresult(itemFactory->CreateForMonitor(monitor, guid_of<GraphicsCaptureItem>(), put_abi(item)));
                auto dxgi=device.device.as<IDXGIDevice>(); com_ptr<IInspectable> inspectable;
                check_hresult(CreateDirect3D11DeviceFromDXGIDevice(dxgi.get(), inspectable.put()));
                auto pool=Direct3D11CaptureFramePool::CreateFreeThreaded(inspectable.as<IDirect3DDevice>(),
                    DirectXPixelFormat::B8G8R8A8UIntNormalized, 2, item.Size());
                auto session=pool.CreateCaptureSession(item);
                auto state=frames;
                auto token=pool.FrameArrived([state](auto const& source, auto const&) {
                    try { auto frame=source.TryGetNextFrame(); std::lock_guard lock(state->mutex);
                        if (state->stopped) { if (frame) frame.Close(); return; }
                        if (state->latest) state->latest.Close(); state->latest=std::move(frame); state->ready.notify_one(); } catch (...) { }
                });
                auto closedToken=item.Closed([state](auto const&, auto const&) { state->closed=true; });
                Cleanup cleanup{[&] {
                    { std::lock_guard lock(state->mutex); state->stopped=true; }
                    session.Close(); pool.FrameArrived(token); pool.Close(); item.Closed(closedToken);
                    { std::lock_guard lock(state->mutex); if (state->latest) state->latest.Close(); state->latest=nullptr; }
                }};
                auto size=item.Size(); Processor converter(device, size.Width, size.Height, w, h);
                auto nv12=device.texture(w,h,DXGI_FORMAT_NV12,D3D11_BIND_RENDER_TARGET);
                auto staging=device.texture(w,h,DXGI_FORMAT_NV12,0,D3D11_USAGE_STAGING,D3D11_CPU_ACCESS_READ);
                Encoder encoder(device,w,h,fps,rate,software,[=](IMFSample* sample, const std::vector<uint8_t>& header) {
                    com_ptr<IMFMediaBuffer> buffer; check_hresult(sample->ConvertToContiguousBuffer(buffer.put()));
                    BYTE* bytes; DWORD count; check_hresult(buffer->Lock(&bytes,nullptr,&count));
                    LONGLONG time=0; sample->GetSampleTime(&time);
                    int key=MFGetAttributeUINT32(sample,MFSampleExtension_CleanPoint,FALSE);
                    cb(user, bytes, static_cast<int>(count), time, key, header.data(), static_cast<int>(header.size())); buffer->Unlock();
                });
                hardware=encoder.hardware ? 1 : 0;
                session.StartCapture(); promise.set_value(S_OK); notified=true;
                auto next=steady_clock::now(); int appliedRate=rate; int64_t index=0; bool haveFrame=false;
                while (!stop.stop_requested() && !state->closed) {
                    encoder.pump();
                    Direct3D11CaptureFrame frame{nullptr};
                    { std::unique_lock lock(state->mutex); state->ready.wait_for(lock, milliseconds(2), [&] { return bool(state->latest)||stop.stop_requested(); });
                        if (steady_clock::now()<next) { lock.unlock(); std::this_thread::sleep_for(milliseconds(1)); continue; }
                        frame=std::move(state->latest); state->latest=nullptr;
                    }
                    if (frame) {
                        if (frame.ContentSize().Width!=size.Width || frame.ContentSize().Height!=size.Height) {
                            frame.Close(); throw_hresult(DXGI_ERROR_MODE_CHANGE_IN_PROGRESS);
                        }
                        auto access=frame.Surface().as<::Windows::Graphics::DirectX::Direct3D11::IDirect3DDxgiInterfaceAccess>(); com_ptr<ID3D11Texture2D> source;
                        check_hresult(access->GetInterface(__uuidof(ID3D11Texture2D),source.put_void()));
                        converter.convert(source.get(),nv12.get()); frame.Close(); haveFrame=true;
                    }
                    if (!haveFrame) continue;
                    if (bitrate!=appliedRate) { appliedRate=bitrate; if (FAILED(encoder.setting(CODECAPI_AVEncCommonMeanBitRate,appliedRate))) reconfigure=1; }
                    if (keyframe.exchange(0) && FAILED(encoder.setting(CODECAPI_AVEncVideoForceKeyFrame,1))) reconfigure=1;
                    com_ptr<IMFSample> sample;
                    if (encoder.gpu) {
                        // Own a texture per in-flight sample: async MFT may retain it after ProcessInput returns.
                        auto copy=device.texture(w,h,DXGI_FORMAT_NV12,D3D11_BIND_RENDER_TARGET);
                        device.context->CopyResource(copy.get(),nv12.get());
                        com_ptr<IMFMediaBuffer> buffer; check_hresult(MFCreateDXGISurfaceBuffer(__uuidof(ID3D11Texture2D),copy.get(),0,FALSE,buffer.put()));
                        check_hresult(buffer->SetCurrentLength(w*h*3/2));
                        check_hresult(MFCreateSample(sample.put())); check_hresult(sample->AddBuffer(buffer.get()));
                    } else {
                        device.context->CopyResource(staging.get(),nv12.get());
                        D3D11_MAPPED_SUBRESOURCE mapped{}; check_hresult(device.context->Map(staging.get(),0,D3D11_MAP_READ,0,&mapped));
                        sample=sample_with_buffer(w*h*3/2); com_ptr<IMFMediaBuffer> buffer; sample->GetBufferByIndex(0,buffer.put());
                        BYTE* bytes; check_hresult(buffer->Lock(&bytes,nullptr,nullptr));
                        for (int row=0;row<h*3/2;++row) memcpy(bytes+row*w,static_cast<BYTE*>(mapped.pData)+row*mapped.RowPitch,w);
                        buffer->Unlock(); buffer->SetCurrentLength(w*h*3/2); device.context->Unmap(staging.get(),0);
                    }
                    sample->SetSampleTime(index*10000000/fps); sample->SetSampleDuration(10000000/fps); ++index;
                    encoder.push(sample.get()); next=steady_clock::now()+microseconds(1000000/fps);
                }
                if (state->closed && !stop.stop_requested()) error(user, static_cast<int>(E_ABORT));
            } catch (...) { auto hr=to_hresult(); if (!notified) promise.set_value(hr); else error(user,hr); }
        });
        auto hr=result.get(); if (FAILED(hr)) { thread.request_stop(); thread.join(); throw_hresult(hr); }
    }
    ~Capture() { thread.request_stop(); frames->ready.notify_all(); if (thread.joinable()) thread.join(); }
};

// WinUI 3's native swapchain interface (not the UWP interface with a different IID).
struct __declspec(uuid("63aad0b8-7c24-40ff-85a8-640d944cc325")) PanelNative : IUnknown {
    virtual HRESULT __stdcall SetSwapChain(IDXGISwapChain*)=0;
};
struct Renderer {
    Device device;
    com_ptr<IMFTransform> decoder;
    com_ptr<IDXGISwapChain1> swapchain;
    std::unique_ptr<Processor> processor;
    int width,height;
    uint64_t frames=0;
    std::mutex mutex;
    Renderer(int w,int h) : width(w),height(h) {
        init_mf();
        check_hresult(CoCreateInstance(CLSID_CMSH264DecoderMFT,nullptr,CLSCTX_INPROC_SERVER,__uuidof(IMFTransform),decoder.put_void()));
        com_ptr<IMFAttributes> attrs; check_hresult(decoder->GetAttributes(attrs.put())); attrs->SetUINT32(MF_LOW_LATENCY,TRUE);
        check_hresult(decoder->ProcessMessage(MFT_MESSAGE_SET_D3D_MANAGER,reinterpret_cast<ULONG_PTR>(device.manager.get())));
        auto in=video_type(MFVideoFormat_H264,w,h,30); check_hresult(decoder->SetInputType(0,in.get(),0));
        output_type();
        auto dxgi=device.device.as<IDXGIDevice>(); com_ptr<IDXGIAdapter> adapter; check_hresult(dxgi->GetAdapter(adapter.put()));
        com_ptr<IDXGIFactory2> factory; check_hresult(adapter->GetParent(__uuidof(IDXGIFactory2),factory.put_void()));
        DXGI_SWAP_CHAIN_DESC1 desc{}; desc.Width=w; desc.Height=h; desc.Format=DXGI_FORMAT_B8G8R8A8_UNORM;
        desc.BufferUsage=DXGI_USAGE_RENDER_TARGET_OUTPUT; desc.BufferCount=2; desc.SampleDesc.Count=1;
        desc.SwapEffect=DXGI_SWAP_EFFECT_FLIP_SEQUENTIAL; desc.AlphaMode=DXGI_ALPHA_MODE_IGNORE;
        check_hresult(factory->CreateSwapChainForComposition(device.device.get(),&desc,nullptr,swapchain.put()));
        check_hresult(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_BEGIN_STREAMING,0));
        check_hresult(decoder->ProcessMessage(MFT_MESSAGE_NOTIFY_START_OF_STREAM,0));
    }
    void output_type() {
        for (DWORD i=0;;++i) {
            com_ptr<IMFMediaType> type; check_hresult(decoder->GetOutputAvailableType(0,i,type.put()));
            GUID subtype; type->GetGUID(MF_MT_SUBTYPE,&subtype);
            if (subtype==MFVideoFormat_NV12) {
                check_hresult(decoder->SetOutputType(0,type.get(),0));
                UINT32 w=0,h=0; check_hresult(MFGetAttributeSize(type.get(),MF_MT_FRAME_SIZE,&w,&h));
                if (w>3840 || h>2160 || w>static_cast<UINT32>((width+15)&~15) || h>static_cast<UINT32>((height+15)&~15)) throw_hresult(MF_E_INVALIDMEDIATYPE);
                processor=std::make_unique<Processor>(device,w,h,width,height); return;
            }
        }
    }
    void drain() {
        for (;;) {
            MFT_OUTPUT_DATA_BUFFER out{}; DWORD status=0;
            HRESULT hr=decoder->ProcessOutput(0,1,&out,&status);
            if (out.pEvents) out.pEvents->Release();
            com_ptr<IMFSample> sample; sample.attach(out.pSample);
            if (hr==MF_E_TRANSFORM_NEED_MORE_INPUT) return;
            if (hr==MF_E_TRANSFORM_STREAM_CHANGE) { output_type(); continue; }
            check_hresult(hr);
            if (!sample) continue;
            com_ptr<IMFMediaBuffer> buffer; check_hresult(sample->GetBufferByIndex(0,buffer.put()));
            auto surface=buffer.as<IMFDXGIBuffer>(); com_ptr<ID3D11Texture2D> texture;
            check_hresult(surface->GetResource(__uuidof(ID3D11Texture2D),texture.put_void()));
            UINT slice=0; check_hresult(surface->GetSubresourceIndex(&slice));
            com_ptr<ID3D11Texture2D> back; check_hresult(swapchain->GetBuffer(0,__uuidof(ID3D11Texture2D),back.put_void()));
            processor->convert(texture.get(),back.get(),slice); check_hresult(swapchain->Present(0,0)); ++frames;
        }
    }
    void push(const uint8_t* data,int size,int64_t timestamp) {
        std::lock_guard lock(mutex);
        auto sample=sample_with_buffer(size); com_ptr<IMFMediaBuffer> buffer; sample->GetBufferByIndex(0,buffer.put());
        BYTE* bytes; check_hresult(buffer->Lock(&bytes,nullptr,nullptr)); memcpy(bytes,data,size);
        buffer->Unlock(); buffer->SetCurrentLength(size); sample->SetSampleTime(timestamp); sample->SetSampleDuration(333333);
        HRESULT hr=decoder->ProcessInput(0,sample.get(),0);
        if (hr==MF_E_NOTACCEPTING) { drain(); hr=decoder->ProcessInput(0,sample.get(),0); }
        check_hresult(hr); drain();
    }
    ~Renderer() { decoder->ProcessMessage(MFT_MESSAGE_COMMAND_FLUSH,0); }
};
template<typename F> static HRESULT boundary(F&& f) noexcept { try { f(); return S_OK; } catch (...) { return to_hresult(); } }
API HRESULT __cdecl coconut_capture_create(HMONITOR monitor,int width,int height,int fps,int bitrate,int software,
    FrameCallback cb,ErrorCallback error,void* user,void** handle) noexcept {
    if (!handle || !cb || !error || width<160 || width>3840 || height<90 || height>2160 || width%2 || height%2 ||
        fps<1 || fps>60 || bitrate<128000 || bitrate>20000000) return E_INVALIDARG;
    *handle=nullptr; return boundary([&] { *handle=new Capture(monitor,width,height,fps,bitrate,software!=0,cb,error,user); });
}
API void __cdecl coconut_capture_destroy(Capture* capture) noexcept { delete capture; }
API int __cdecl coconut_capture_hardware(Capture* capture) noexcept { return capture ? capture->hardware.load() : 0; }
API int __cdecl coconut_capture_reconfigure(Capture* capture) noexcept { return capture ? capture->reconfigure.load() : 0; }
API void __cdecl coconut_capture_bitrate(Capture* capture,int bitrate) noexcept { if (capture && bitrate>=128000 && bitrate<=20000000) capture->bitrate=bitrate; }
API void __cdecl coconut_capture_keyframe(Capture* capture) noexcept { if (capture) capture->keyframe=1; }
API HRESULT __cdecl coconut_renderer_create(int width,int height,void** handle) noexcept {
    if (!handle || width<160 || height<90 || width>3840 || height>2160 || width%2 || height%2) return E_INVALIDARG;
    *handle=nullptr; return boundary([&] { init_mf(); *handle=new Renderer(width,height); });
}
API HRESULT __cdecl coconut_renderer_attach(Renderer* renderer,IUnknown* panel) noexcept {
    if (!renderer || !panel) return E_INVALIDARG;
    return boundary([&] { com_ptr<PanelNative> native; check_hresult(panel->QueryInterface(__uuidof(PanelNative),native.put_void()));
        check_hresult(native->SetSwapChain(renderer->swapchain.get())); });
}
API HRESULT __cdecl coconut_panel_clear(IUnknown* panel) noexcept {
    if (!panel) return E_INVALIDARG;
    return boundary([&] { com_ptr<PanelNative> native; check_hresult(panel->QueryInterface(__uuidof(PanelNative),native.put_void())); check_hresult(native->SetSwapChain(nullptr)); });
}
API HRESULT __cdecl coconut_renderer_push(Renderer* renderer,const uint8_t* data,int size,int64_t time) noexcept {
    if (!renderer || !data || size<1 || size>4*1024*1024) return E_INVALIDARG;
    return boundary([&] { init_mf(); renderer->push(data,size,time); });
}
API uint64_t __cdecl coconut_renderer_frames(Renderer* renderer) noexcept { if (!renderer) return 0; std::lock_guard lock(renderer->mutex); return renderer->frames; }
API void __cdecl coconut_renderer_destroy(Renderer* renderer) noexcept { delete renderer; }
