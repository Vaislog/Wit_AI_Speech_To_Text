Imports System.IO
Imports System.Net
Imports NAudio.Wave

Public Class WitAi_SpeechToText
    Private MaxDuration As Integer = 5
    Private AccessTokens As IEnumerator
    Public TempDir As String = Application.StartupPath & "\Temp\"
    Private ClrTemp As Boolean = False
    Private Err As Boolean
    Private ErrText As String

    Public Sub New(ByVal Max As UShort, ByVal Tokens() As String, Optional ClearTemp As Boolean = False)
        MaxDuration = IIf(Max < 11, Max, 10)
        AccessTokens = Tokens.GetEnumerator
        ClrTemp = ClearTemp
        Err = False
        ErrText = ""
    End Sub

    Private Function ProcessSpeechStream(ByVal stream As Stream, ByVal type As String) As String
        Dim arr() As Byte
        Using ms As New MemoryStream()
            stream.CopyTo(ms)
            arr = ms.ToArray()
        End Using
        If AccessTokens.MoveNext = False Then
            AccessTokens.Reset()
            AccessTokens.MoveNext()
        End If
        Dim appAccessToken As String = AccessTokens.Current
        Dim request As HttpWebRequest = CType(WebRequest.Create("https://api.wit.ai/speech"), HttpWebRequest)
        request.Proxy = New WebProxy
        request.SendChunked = True
        request.Method = "POST"
        request.Headers("Authorization") = "Bearer " & appAccessToken
        request.ContentType = type
        request.ContentLength = arr.Length
        Using st As Stream = request.GetRequestStream()
            st.Write(arr, 0, arr.Length)
        End Using
        Try
            Dim response As HttpWebResponse = CType(request.GetResponse(), HttpWebResponse)
            If response.StatusCode = HttpStatusCode.OK Then
                Dim response_stream As New StreamReader(response.GetResponseStream())
                Return response_stream.ReadToEnd()
            Else
                Err = True
                ErrText = ResponseStatus(response.StatusCode)
                Return ErrText
            End If
        Catch ex As exception
            Err = True
            ErrText = ResponseStatus(GetCodeFromErrorText(ex.Message))
            Return ErrText
        End Try
    End Function
    Private Function GetCodeFromErrorText(ByVal text As String) As Integer
        If text.Length > 0 Then
            Dim Result As String = New String(text.Where(Function(e) Char.IsDigit(e)).ToArray)
            If Result.Length > 0 Then
                Return CInt(Result)
            Else
                Return -1
            End If
        End If
        Return -1
    End Function
    Public Async Function TextFromVoice(ByVal Path As String) As Task(Of String)
        Dim Result As String = Await Task.Run(Function() GetTextFromVoice(Path))
        If Err Then Result = ErrText
        Return Result
    End Function

    Private Function GetTextFromVoice(ByVal PathToFile As String) As String
        Dim CurrentIndex As Integer = 0
        Dim Files As New List(Of String)
        Dim CurrentFile As String = PathToFile
        Dim Type As String = ""
        Dim CurrentExt As String = IO.Path.GetExtension(CurrentFile)
        Select Case CurrentExt
            Case ".mp3"
                Type = "audio/mpeg3"
            Case ".wav"
                Type = "audio/wav"
        End Select
        Dim Duration As Long = 0
        Using dr As New AudioFileReader(CurrentFile)
            Duration = dr.TotalTime.TotalSeconds
        End Using
        If Duration > MaxDuration Then
            Dim ChunksCount As Integer = Duration \ MaxDuration
            If IO.Directory.Exists(TempDir) = False Then
                IO.Directory.CreateDirectory(TempDir)
            End If
            For i = 0 To ChunksCount
                Files.Add(TempDir & CurrentIndex & CurrentExt)

                If Type = "audio/mpeg3" Then
                    TrimMp3(CurrentIndex, CurrentFile, TempDir & CurrentIndex & CurrentExt)
                ElseIf Type = "audio/wav" Then
                    TrimWav(CurrentIndex, CurrentFile, TempDir & CurrentIndex & CurrentExt)
                End If
                CurrentIndex += 1
            Next
            Dim Result As String = GetAllText(Files, Type).Result.ToString
            If ClrTemp Then
                For i = 0 To Files.Count - 1
                    IO.File.Delete(Files(i))
                Next
            End If
            Return Result
        Else
            Using fs As New FileStream(CurrentFile, FileMode.Open, FileAccess.Read)
                Return ExtractFromJson(ProcessSpeechStream(fs, Type))
            End Using
        End If

    End Function

    Private Async Function GetAllText(ByVal Files As List(Of String), ByVal Type As String) As Task(Of String)
        Dim parallelTasks As New List(Of Task(Of String))
        For i = 0 To Files.Count - 1
            Dim index As Integer = i
            parallelTasks.Add(Task.Factory.StartNew(Function() GetAnswer(Files(index), Type), TaskCreationOptions.PreferFairness))
        Next
        Task.WaitAll(parallelTasks.ToArray)
        Await Task.Delay(1)
        Dim finalResult As String = ""
        For Each partialResult In parallelTasks.Select(Function(t) t.Result)
            finalResult &= partialResult & " "
        Next partialResult
        Return finalResult
    End Function

    Private Function GetAnswer(ByVal FilePath As String, ByVal Type As String) As String
        Using fs As New FileStream(FilePath, FileMode.Open, FileAccess.Read)
            If Err Then
                Return Nothing
            Else
                Return ExtractFromJson(ProcessSpeechStream(fs, Type))
            End If
        End Using
    End Function

    Private Function ExtractFromJson(ByVal str As String) As String
        Dim FindStr As String = """text"": """
        If str.Contains(FindStr) Then
            Return Split(Split(str, FindStr)(1), Chr(34))(0)
        Else
            Return String.Empty
        End If
    End Function

    Private Sub TrimWav(ByVal CurrentIndex As Integer, ByVal open As String, ByVal save As String)
        Using reader As New WaveFileReader(open)
            Using writer As New WaveFileWriter(save, reader.WaveFormat)
                Dim bytesPerMillisecond As Integer = reader.WaveFormat.AverageBytesPerSecond / 1000
                Dim startPos As Integer = TimeSpan.FromSeconds(CurrentIndex * MaxDuration).TotalMilliseconds * bytesPerMillisecond
                startPos = startPos - startPos Mod reader.WaveFormat.BlockAlign
                Dim endPos As Integer = startPos + CInt(Math.Truncate(TimeSpan.FromSeconds(MaxDuration).TotalMilliseconds)) * bytesPerMillisecond
                If endPos > reader.Length Then endPos = reader.Length
                reader.Position = startPos
                Dim buffer((reader.BlockAlign * 1024) - 1) As Byte
                Do While reader.Position < endPos
                    Dim bytesRequired As Integer = CInt(endPos - reader.Position)
                    If bytesRequired > 0 Then
                        Dim bytesToRead As Integer = Math.Min(bytesRequired, buffer.Length)
                        Dim bytesRead As Integer = reader.Read(buffer, 0, bytesToRead)
                        If bytesRead > 0 Then
                            writer.Write(buffer, 0, bytesRead)
                        End If
                    End If
                Loop
            End Using
        End Using
    End Sub

    Private Sub TrimMp3(ByVal CurrentIndex As Integer, ByVal open As String, ByVal save As String)
        Using mp3FileReader = New Mp3FileReader(open)
            Using writer As FileStream = File.Create(save)
                Dim startPostion As TimeSpan = TimeSpan.FromSeconds(CurrentIndex * MaxDuration)
                Dim endPostion As TimeSpan = TimeSpan.FromSeconds(CurrentIndex * MaxDuration + MaxDuration)
                mp3FileReader.CurrentTime = startPostion
                Do While mp3FileReader.CurrentTime < endPostion
                    Dim frame As Mp3Frame = mp3FileReader.ReadNextFrame()
                    If frame Is Nothing Then Exit Do
                    writer.Write(frame.RawData, 0, frame.RawData.Length)
                Loop
            End Using
        End Using
    End Sub
    Private Function ResponseStatus(ByVal code As Integer) As String
        Select Case code
            Case 200
                Return "Code 200: OK"
            Case 400
                Return "Code 400: missing body (code: body) or missing content-type (code: content-type) or unknown content-type (code: unknown-content-type) or speech recognition failed (code: speech-rec) or invalid parameters (code: invalid-params)"
            Case 401
                Return "Code 401: missing or wrong auth token (code: auth)"
            Case 408
                Return "Code 408: request timed out, client was to slow to send data or audio is too long (code: timeout)"
            Case 500
                Return "Code 500: something went wrong on our side, our experts are probably fixing it. (code: wit)"
            Case 501
                Return "Code 501: something is very wrong on our side, our experts are probably being yelled at"
            Case Else
                Return "Unknown Status"
        End Select
    End Function

End Class
