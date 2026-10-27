"""Offline credential-isolation and HTTP-contract checks; never contact a provider."""
import base64
from contextlib import redirect_stderr, redirect_stdout
from email.parser import BytesParser
from email.policy import default
import importlib.util
import io
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from unittest.mock import patch

import imagegen


class ImagegenTests(unittest.TestCase):
    def test_project_configuration_normalizes_url(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / '.env.imagegen.example').write_text(
                'OPENAI_BASE_URL=https://tc-paperhub.diezhi.net/v1/\n'
                'OPENAI_IMAGE_MODEL=gpt-image-2\nOPENAI_API_KEY=\n', encoding='utf-8')
            (root / '.env.imagegen.local').write_text(
                '\ufeffOPENAI_API_KEY="literal-$()-test"\n', encoding='utf-8')
            settings = imagegen.load_settings(root)
            self.assertEqual('https://tc-paperhub.diezhi.net/v1', settings['OPENAI_BASE_URL'])
            self.assertEqual('literal-$()-test', settings['OPENAI_API_KEY'])

    def test_other_account_credentials_are_not_inherited(self):
        settings = {'OPENAI_BASE_URL': 'https://tc-paperhub.diezhi.net/v1', 'OPENAI_API_KEY': ''}
        with patch.dict(os.environ, {'OPENAI_API_KEY': 'unrelated-secret', 'OPENAI_ORG_ID': 'other-org', 'OPENAI_PROJECT_ID': 'other-project'}):
            child = imagegen.child_environment(settings)
            self.assertNotIn('OPENAI_API_KEY', child)
            self.assertNotIn('OPENAI_ORG_ID', child)
            self.assertNotIn('OPENAI_PROJECT_ID', child)
            self.assertEqual('unrelated-secret', os.environ['OPENAI_API_KEY'])
            settings['OPENAI_API_KEY'] = 'project-test-key'
            self.assertEqual('project-test-key', imagegen.child_environment(settings)['OPENAI_API_KEY'])

    def test_missing_key_prevents_live_subprocess(self):
        settings = {'OPENAI_BASE_URL': 'https://tc-paperhub.diezhi.net/v1', 'OPENAI_IMAGE_MODEL': 'gpt-image-2', 'OPENAI_API_KEY': ''}
        with patch.object(imagegen, 'load_settings', return_value=settings), patch.object(imagegen.subprocess, 'run') as run:
            with self.assertRaisesRegex(ValueError, 'Fill OPENAI_API_KEY'):
                imagegen.main(['generate', '--prompt', 'test', '--out', 'test.png'])
            run.assert_not_called()

    def test_launcher_passes_options_and_redacts_provider_error(self):
        settings = {'OPENAI_BASE_URL': 'https://tc-paperhub.diezhi.net/v1', 'OPENAI_IMAGE_MODEL': 'gpt-image-2', 'OPENAI_API_KEY': 'project-test-key'}
        result = subprocess.CompletedProcess([], 1, 'echo project-test-key', 'error project-test-key')
        output, error = io.StringIO(), io.StringIO()
        with patch.object(imagegen, 'load_settings', return_value=settings), patch.object(imagegen.subprocess, 'run', return_value=result) as run, redirect_stdout(output), redirect_stderr(error):
            code = imagegen.main(['edit', '--model', 'gpt-image-2.5-sunburst', '--image', 'input.png', '--prompt', 'test', '--out', 'out.png'])
        self.assertEqual(1, code)
        self.assertNotIn('project-test-key', output.getvalue() + error.getvalue())
        self.assertIn('[REDACTED]', error.getvalue())
        self.assertEqual('gpt-image-2.5-sunburst', run.call_args.args[0][-7])
        self.assertEqual('project-test-key', run.call_args.kwargs['env']['OPENAI_API_KEY'])

    def test_sdk_generation_and_multipart_edits_for_all_models(self):
        import httpx
        from openai import OpenAI

        spec = importlib.util.spec_from_file_location('bundled_imagegen', imagegen.skill_path())
        cli = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(cli)
        png = base64.b64decode('iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+aC1sAAAAASUVORK5CYII=')
        requests = []

        def respond(request):
            requests.append(request)
            return httpx.Response(200, json={'created': 1, 'data': [{'b64_json': base64.b64encode(png).decode()}]})

        with tempfile.TemporaryDirectory() as directory, OpenAI(api_key='offline-test-key', base_url='https://tc-paperhub.diezhi.net/v1/', http_client=httpx.Client(transport=httpx.MockTransport(respond))) as client:
            root = Path(directory)
            source = root / 'input.png'
            source.write_bytes(png)
            for model in imagegen.MODELS:
                for operation in ('generate', 'edit'):
                    with self.subTest(model=model, operation=operation):
                        out = root / f'{model}-{operation}.png'
                        args = ['image_gen.py', operation, '--model', model, '--prompt', '中文测试 prompt', '--out', str(out), '--no-augment']
                        if operation == 'edit':
                            args += ['--image', str(source), '--image', str(source), '--mask', str(source)]
                        with patch.object(sys, 'argv', args), patch.dict(os.environ, {'OPENAI_API_KEY': 'offline-test-key'}), patch.object(cli, '_create_client', return_value=client), redirect_stdout(io.StringIO()), redirect_stderr(io.StringIO()):
                            self.assertEqual(0, cli.main())
                        self.assertEqual(png, out.read_bytes())
                        request = requests[-1]
                        self.assertEqual('Bearer offline-test-key', request.headers['authorization'])
                        suffix = 'edits' if operation == 'edit' else 'generations'
                        self.assertEqual('https://tc-paperhub.diezhi.net/v1/images/' + suffix, str(request.url))
                        if operation == 'generate':
                            body = json.loads(request.content)
                            self.assertEqual(model, body['model'])
                            self.assertEqual('中文测试 prompt', body['prompt'])
                        else:
                            message = BytesParser(policy=default).parsebytes(('Content-Type: ' + request.headers['content-type'] + '\r\n\r\n').encode() + request.content)
                            fields = {}
                            for part in message.iter_parts():
                                fields.setdefault(part.get_param('name', header='content-disposition'), []).append(part.get_payload(decode=True))
                            self.assertEqual([model.encode()], fields['model'])
                            self.assertEqual([png, png], fields['image[]'])
                            self.assertEqual([png], fields['mask'])
        self.assertEqual(6, len(requests))


if __name__ == '__main__':
    unittest.main()
