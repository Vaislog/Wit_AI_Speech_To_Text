Public Class Form1
    Dim s As New Stopwatch
    Dim Tokens() As String = {"token1", "token2"} 'можно и один
    Dim Wit As WitAi_SpeechToText

    Private Async Sub Button1_Click(sender As Object, e As EventArgs) Handles Button1.Click
        Dim OFD As New OpenFileDialog With {.Filter = "Mp3|*.mp3|Wav|*.wav"}
        If OFD.ShowDialog = DialogResult.OK Then
            ProgressBar1.Visible = True
            RichTextBox1.Clear()
            TextBox1.Text = OFD.FileName
            s.Reset()
            s.Start()
            Timer1.Start()
            Wit = New WitAi_SpeechToText(10, Tokens, True) 'максимальная длительность отрезков (максимум 10 секунд), массив токенов, удалять ли после распознания нарезки
            RichTextBox1.Text = Await Wit.TextFromVoice(OFD.FileName)
            s.Stop()
            Timer1.Stop()
            ProgressBar1.Visible = False
        End If
    End Sub

    Private Sub Timer1_Tick(sender As Object, e As EventArgs) Handles Timer1.Tick
        Label2.Text = s.Elapsed.ToString("mm\:ss\:fff")
    End Sub

End Class
