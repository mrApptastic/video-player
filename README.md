# video-player

this is a video player that keeps track of where you are in a series
smart video player der husker hvor du er i episoderne
episoderne loader fra [docs/episodes.json](docs/episodes.json) som du kan genskabe med converteren

workflow
- smid dine videoer i [videos](videos) mappen den er tracked via gitkeep og resten ignoreres
- kør converter med dotnet run --project video-converter kræver net 8 sdk ffprobe for længde og ffmpeg for thumb
- åbn [docs/index.html](docs/index.html) via en lokal server så browseren kan hente episodes.json

notes
- converteren laver data urls så filen kan blive stor hvis du bruger tunge videoer
