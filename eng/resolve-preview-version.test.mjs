import assert from 'node:assert/strict';
import test from 'node:test';
import { chooseVersion, readPublishedVersions, readVersions } from './resolve-preview-version.mjs';

test('first publish uses the configured preview; later publishes increment numerically', () => {
  assert.equal(chooseVersion('0.1.0-preview.1', []), '0.1.0-preview.1');
  assert.equal(chooseVersion('0.1.0-preview.1', ['0.1.0-preview.1']), '0.1.0-preview.2');
  assert.equal(chooseVersion('0.1.0-preview.1', ['0.1.0-preview.9', '0.1.0-preview.10']), '0.1.0-preview.11');
});

test('configured preview is a floor and other release lines do not affect it', () => {
  assert.equal(chooseVersion('0.1.0-preview.4', [
    '0.1.0-preview.1', '0.2.0-preview.99', '0.1.0', '0.1.0-rc.9',
  ]), '0.1.0-preview.4');
});

test('only unused previews on the configured release line are accepted', () => {
  const published = ['0.1.0-preview.1'];
  assert.equal(chooseVersion('0.1.0-preview.1', published, { suffix: 'preview.3' }), '0.1.0-preview.3');
  assert.equal(chooseVersion('0.1.0-preview.1', published, { tag: 'v0.1.0-preview.2' }), '0.1.0-preview.2');
  for (const suffix of ['preview.1', 'rc.2', 'preview.0', 'preview.02', 'preview.2;evil']) {
    assert.throws(() => chooseVersion('0.1.0-preview.1', published, { suffix }));
  }
  for (const tag of ['v0.1.0', 'v0.1.0-rc.2', 'v0.2.0-preview.2', 'v0.1.0-preview.1']) {
    assert.throws(() => chooseVersion('0.1.0-preview.1', published, { tag }));
  }
  assert.throws(() => chooseVersion('0.1.0', published));
});

function fakeFeed(responses) {
  return async (url, options) => {
    assert.ok(options.signal);
    if (url === 'https://feed/index.json') return Response.json({
      resources: [{ '@type': 'PackageBaseAddress/3.0.0', '@id': 'https://feed/flat/' }],
    });
    assert.ok(Object.hasOwn(responses, url), `Unexpected request ${url}`);
    const response = responses[url];
    return typeof response === 'number' ? new Response(null, { status: response }) : Response.json(response);
  };
}

test('all packages contribute, including a partially published newer preview', async () => {
  const versions = await readVersions('https://feed/index.json', ['Core', 'Provider', 'New'], {}, fakeFeed({
    'https://feed/flat/core/index.json': { versions: ['0.1.0-preview.1'] },
    'https://feed/flat/provider/index.json': { versions: ['0.1.0-preview.1', '0.1.0-preview.2'] },
    'https://feed/flat/new/index.json': 404,
  }));
  assert.equal(chooseVersion('0.1.0-preview.1', versions), '0.1.0-preview.3');
});

test('feed failures and malformed responses stop publication', async () => {
  for (const response of [401, 403, 429, 500, {}, { versions: [2] }]) {
    await assert.rejects(readVersions('https://feed/index.json', ['Core'], {}, fakeFeed({
      'https://feed/flat/core/index.json': response,
    })));
  }
  await assert.rejects(readVersions('https://feed/index.json', ['Core'], {}, async () => {
    throw new Error('Network unavailable');
  }));
  await assert.rejects(readVersions('https://feed/index.json', ['Core'], {}, async () => Response.json({})));
});

function fakeFeeds(feeds) {
  return async (url, options) => {
    assert.ok(options.signal);
    for (const [index, { flat, versions, authorization }] of Object.entries(feeds)) {
      if (authorization) assert.equal(options.headers?.authorization, authorization);
      if (url === index) return Response.json({
        resources: [{ '@type': 'PackageBaseAddress/3.0.0', '@id': flat }],
      });
      if (url.startsWith(flat)) {
        const id = url.slice(flat.length).split('/')[0];
        return Object.hasOwn(versions, id)
          ? Response.json({ versions: versions[id] }) : new Response(null, { status: 404 });
      }
    }
    assert.fail(`Unexpected request ${url}`);
  };
}

const feeds = (nuget) => ({
  'https://api.nuget.org/v3/index.json': {
    flat: 'https://api.nuget.org/v3-flatcontainer/', versions: nuget,
  },
});

test('the next preview is determined by published packages on NuGet.org', async () => {
  let versions = await readPublishedVersions(['Core'], {}, fakeFeeds(feeds(
    { core: ['0.1.0-preview.1', '0.1.0-preview.2', '0.1.0-preview.3'] })));
  assert.equal(chooseVersion('0.1.0-preview.2', versions), '0.1.0-preview.4');
  assert.throws(() => chooseVersion('0.1.0-preview.2', versions, { tag: 'v0.1.0-preview.3' }));
  assert.throws(() => chooseVersion('0.1.0-preview.2', versions, { suffix: 'preview.3' }));

  // Multiple packages with different preview levels
  versions = await readPublishedVersions(['Core', 'Provider'], {}, fakeFeeds(feeds(
    { core: ['0.1.0-preview.4'], provider: ['0.1.0-preview.6'] })));
  assert.equal(chooseVersion('0.1.0-preview.2', versions), '0.1.0-preview.7');
});

test('NuGet.org feed errors stop publication', async () => {
  await assert.rejects(readPublishedVersions(['Core'], {}, async () => new Response(null, { status: 500 })));
});
