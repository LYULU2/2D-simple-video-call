using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Unity.WebRTC;
using Unity.WebRTC.Samples;
using UnityEngine.UI;
using Button = UnityEngine.UI.Button;

class PeerConnectionSample : MonoBehaviour
{
#pragma warning disable 0649
    [SerializeField] private Button startButton;
    [SerializeField] private Button callButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button hangUpButton;
    [SerializeField] private Button pickUpButton;
    [SerializeField] private Text localCandidateId;
    [SerializeField] private Text remoteCandidateId;

    [SerializeField] private Camera cam;
    [SerializeField] static WebCamTexture backcam;
    [SerializeField] private RawImage sourceImage;
    [SerializeField] private RawImage receiveImage;
    [SerializeField] private Transform rotateObject;
#pragma warning restore 0649

    private RTCPeerConnection _pc1, _pc2;
    private RTCDataChannel dataChannel, remoteDataChannel;
    private DelegateOnOpen onDataChannelOpen;
    private DelegateOnClose onDataChannelClose;
    private DelegateOnDataChannel onDataChannel;
    private List<RTCRtpSender> pc1Senders;
    private MediaStream videoStream, receiveStream;
    private DelegateOnIceConnectionChange pc1OnIceConnectionChange;
    private DelegateOnIceConnectionChange pc2OnIceConnectionChange;
    private DelegateOnIceCandidate pc1OnIceCandidate;
    private DelegateOnIceCandidate pc2OnIceCandidate;
    private DelegateOnTrack pc2Ontrack;
    private DelegateOnNegotiationNeeded pc1OnNegotiationNeeded;
    private bool videoUpdateStarted;


    private VideoStreamTrack videoStreamTrack;

    private const int width = 1280;
    private const int height = 720;

    private void Awake()
    {
        WebRTC.Initialize(WebRTCSettings.EncoderType, WebRTCSettings.LimitTextureSize);
        startButton.onClick.AddListener(OnStart);
        callButton.onClick.AddListener(Call);
        restartButton.onClick.AddListener(RestartIce);
        hangUpButton.onClick.AddListener(HangUp);
        pickUpButton.onClick.AddListener(pickUp);
        receiveStream = new MediaStream();
    }

    private void OnDestroy()
    {
        WebRTC.Dispose();
    }

    private void pickUp()
	{
        callButton.interactable = false;
        hangUpButton.interactable = true;

    }

    private void Start()
    {
        pc1Senders = new List<RTCRtpSender>();
        callButton.interactable = false;
        restartButton.interactable = false;
        hangUpButton.interactable = false;
        pickUpButton.interactable = false;

        pc1OnIceConnectionChange = state => { OnIceConnectionChange(_pc1, state); };
        pc2OnIceConnectionChange = state => { OnIceConnectionChange(_pc2, state); };
        pc1OnIceCandidate = candidate => { OnIceCandidate(_pc1, candidate); };
        pc2OnIceCandidate = candidate => { OnIceCandidate(_pc2, candidate); };

        //_pc1.OnTrack = e => receiveVideoStream.AddTrack(e.Track);


        //prepare tracks to the local stream
        //foreach (var track in sourceVideoStream.GetTracks())
        //{
        //    pcLocal.AddTrack(track, sourceVideoStream);
        //}

        pc2Ontrack = e =>
        {
            receiveStream.AddTrack(e.Track);
        };
        

        receiveStream.OnAddTrack = e =>
        {
            if (e.Track is VideoStreamTrack track)
            {
                receiveImage.texture = track.InitializeReceiver(width, height);
                receiveImage.color = Color.white;
            }
        };

    }

    private void OnStart()
    {
        startButton.interactable = false;
        callButton.interactable = true;
        pickUpButton.interactable = true;
        

        if (videoStream == null)
        {
            videoStream = cam.CaptureStream(width, height, 1000000);

        }

        if (backcam == null)
		{
            backcam = new WebCamTexture();
		}
        //sourceImage.texture = cam.targetTexture;
        sourceImage.texture = backcam;
        if (!backcam.isPlaying)
		{
            backcam.Play();
		}
        videoStreamTrack = new VideoStreamTrack("video", backcam);
        sourceImage.color = Color.white;
    }

    private void Update()
    {
        if (rotateObject != null)
        {
            float t = Time.deltaTime;
            rotateObject.Rotate(100 * t, 200 * t, 300 * t);
        }
    }

    private static RTCConfiguration GetSelectedSdpSemantics()
    {
        RTCConfiguration config = default;
        config.iceServers = new[] {new RTCIceServer {urls = new[] {"stun:stun.l.google.com:19302"}}};

        return config;
    }

    private void OnIceConnectionChange(RTCPeerConnection pc, RTCIceConnectionState state)
    {
        Debug.Log($"{GetName(pc)} IceConnectionState: {state}");

        if (state == RTCIceConnectionState.Connected || state == RTCIceConnectionState.Completed)
        {
            StartCoroutine(CheckStats(pc));
        }
    }

    // Display the video codec that is actually used.
    IEnumerator CheckStats(RTCPeerConnection pc)
    {
        yield return new WaitForSeconds(0.1f);
        if (pc == null)
            yield break;

        var op = pc.GetStats();
        yield return op;
        if (op.IsError)
        {
            Debug.LogErrorFormat("RTCPeerConnection.GetStats failed: {0}", op.Error);
            yield break;
        }

        RTCStatsReport report = op.Value;
        RTCIceCandidatePairStats activeCandidatePairStats = null;
        RTCIceCandidateStats remoteCandidateStats = null;

        foreach (var transportStatus in report.Stats.Values.OfType<RTCTransportStats>())
        {
            if (report.Stats.TryGetValue(transportStatus.selectedCandidatePairId, out var tmp))
            {
                activeCandidatePairStats = tmp as RTCIceCandidatePairStats;
            }
        }

        if (activeCandidatePairStats == null || string.IsNullOrEmpty(activeCandidatePairStats.remoteCandidateId))
        {
            yield break;
        }

        foreach (var iceCandidateStatus in report.Stats.Values.OfType<RTCIceCandidateStats>())
        {
            if (iceCandidateStatus.Id == activeCandidatePairStats.remoteCandidateId)
            {
                remoteCandidateStats = iceCandidateStatus;
            }
        }

        if (remoteCandidateStats == null || string.IsNullOrEmpty(remoteCandidateStats.Id))
        {
            yield break;
        }

        Debug.Log($"{GetName(pc)} candidate stats Id:{remoteCandidateStats.Id}, Type:{remoteCandidateStats.candidateType}");
        var updateText = GetName(pc) == "pc1" ? localCandidateId : remoteCandidateId;
        updateText.text = remoteCandidateStats.Id;
    }

    IEnumerator PeerNegotiationNeeded(RTCPeerConnection pc)
    {
        var op = pc.CreateOffer();
        yield return op;

        if (!op.IsError)
        {
            if (pc.SignalingState != RTCSignalingState.Stable)
            {
                Debug.LogError($"{GetName(pc)} signaling state is not stable.");
                yield break;
            }

            yield return StartCoroutine(OnCreateOfferSuccess(pc, op.Desc));
        }
        else
        {
            OnCreateSessionDescriptionError(op.Error);
        }
    }

    private void AddTracks()
    {
        foreach (var track in videoStream.GetTracks())
        {
            //pc1Senders.Add(_pc1.AddTrack(track, videoStream));
            pc1Senders.Add(_pc1.AddTrack(videoStreamTrack));
        }

        if (!videoUpdateStarted)
        {
            StartCoroutine(WebRTC.Update());
            videoUpdateStarted = true;
        }
    }

    private void RemoveTracks()
    {
        foreach (var sender in pc1Senders)
        {
            _pc1.RemoveTrack(sender);
        }

        pc1Senders.Clear();

        MediaStreamTrack[] tracks = receiveStream.GetTracks().ToArray();
        foreach (var track in tracks)
        {
            receiveStream.RemoveTrack(track);
            track.Dispose();
        }
    }

    private void Call()
    {
        callButton.interactable = false;
        hangUpButton.interactable = true;
        restartButton.interactable = true;

        pc1OnNegotiationNeeded = () => { StartCoroutine(PeerNegotiationNeeded(_pc1)); };

        var configuration = GetSelectedSdpSemantics();
        _pc1 = new RTCPeerConnection(ref configuration);
        _pc2 = new RTCPeerConnection(ref configuration);
        _pc1.OnIceCandidate = pc1OnIceCandidate;
        _pc1.OnIceConnectionChange = pc1OnIceConnectionChange;
        _pc1.OnNegotiationNeeded = pc1OnNegotiationNeeded;
        _pc2.OnIceCandidate = pc2OnIceCandidate;
        _pc2.OnIceConnectionChange = pc2OnIceConnectionChange;
        //added
        _pc2.OnDataChannel = onDataChannel;
        _pc2.OnTrack = pc2Ontrack;

        RTCDataChannelInit conf = new RTCDataChannelInit();
        dataChannel = _pc1.CreateDataChannel("data", conf);
        dataChannel.OnOpen = onDataChannelOpen;

        AddTracks();
    }

    private void RestartIce()
    {
        restartButton.interactable = false;

        _pc1.RestartIce();
    }

    private void HangUp()
    {
        RemoveTracks();

        _pc1.Close();
        _pc2.Close();
        _pc1.Dispose();
        _pc2.Dispose();
        _pc1 = null;
        _pc2 = null;

        callButton.interactable = true;
        restartButton.interactable = false;
        hangUpButton.interactable = false;

        receiveImage.color = Color.black;
    }

    private void OnIceCandidate(RTCPeerConnection pc, RTCIceCandidate candidate)
    {
        GetOtherPc(pc).AddIceCandidate(candidate);
        Debug.Log($"{GetName(pc)} ICE candidate:\n {candidate.Candidate}");
    }

    private string GetName(RTCPeerConnection pc)
    {
        return (pc == _pc1) ? "pc1" : "pc2";
    }

    private RTCPeerConnection GetOtherPc(RTCPeerConnection pc)
    {
        return (pc == _pc1) ? _pc2 : _pc1;
    }

    private IEnumerator OnCreateOfferSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Offer from {GetName(pc)}\n{desc.sdp}");
        Debug.Log($"{GetName(pc)} setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        //var otherPc = GetOtherPc(pc);
        
    }

    //remote pc
    private IEnumerator OnReceieveOfferSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        var otherPc = GetOtherPc(pc);
        // how does the remote get the local pc? or sdp info????
        //Debug.Log($"{GetName(otherPc)} setRemoteDescription start");
        Debug.Log($"remote setRemoteDescription start");
        var op2 = pc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
            yield break;
        }

        //Debug.Log($"{GetName(otherPc)} createAnswer start");
        Debug.Log($"remote pc createAnswer start");
        // Since the 'remote' side has no media stream we need
        // to pass in the right constraints in order for it to
        // accept the incoming offer of audio and video.

        var op3 = otherPc.CreateAnswer();
        yield return op3;
        if (!op3.IsError)
        {
            yield return OnCreateAnswerSuccess(otherPc, op3.Desc);
        }
        else
        {
            OnCreateSessionDescriptionError(op3.Error);
        }
    }

    //local pc
    private IEnumerator OnReceieveAnswerSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        var otherPc = GetOtherPc(pc);
        // how to receieve?????
        var op2 = otherPc.SetRemoteDescription(ref desc);
        yield return op2;
        if (!op2.IsError)
        {
            OnSetRemoteSuccess(otherPc);
        }
        else
        {
            var error = op2.Error;
            OnSetSessionDescriptionError(ref error);
        }
    }

    private void OnSetLocalSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"{GetName(pc)} SetLocalDescription complete");
    }

    void OnSetSessionDescriptionError(ref RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
        HangUp();
    }

    private void OnSetRemoteSuccess(RTCPeerConnection pc)
    {
        Debug.Log($"{GetName(pc)} SetRemoteDescription complete");
    }

    // 
    IEnumerator OnCreateAnswerSuccess(RTCPeerConnection pc, RTCSessionDescription desc)
    {
        Debug.Log($"Answer from {GetName(pc)}:\n{desc.sdp}");
        Debug.Log($"{GetName(pc)} setLocalDescription start");
        var op = pc.SetLocalDescription(ref desc);
        yield return op;

        if (!op.IsError)
        {
            OnSetLocalSuccess(pc);
        }
        else
        {
            var error = op.Error;
            OnSetSessionDescriptionError(ref error);
        }

        //var otherPc = GetOtherPc(pc);
        //Debug.Log($"{GetName(otherPc)} setRemoteDescription start");

        
    }

    private static void OnCreateSessionDescriptionError(RTCError error)
    {
        Debug.LogError($"Error Detail Type: {error.message}");
    }
}
