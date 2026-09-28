// All of LocalChat's own JavaScript, and nothing else: code highlighting, copy to clipboard,
// scroll handling, and the composer's Enter key. Blazor imports it as a module.

/**
 * Highlights every code block in `container` that has not been highlighted yet, and adds a toolbar
 * with the language and a Copy button. The toolbar goes inside the <pre>, so the nodes Blazor
 * rendered keep their place.
 */
export function highlightCodeBlocks(container) {
  if (!container) {
    return;
  }

  for (const code of container.querySelectorAll('pre > code')) {
    const pre = code.parentElement;
    if (pre.dataset.enhanced) {
      continue;
    }

    pre.dataset.enhanced = 'true';
    if (window.hljs) {
      window.hljs.highlightElement(code);
    }

    const language = [...code.classList]
      .find(name => name.startsWith('language-'))
      ?.substring('language-'.length);

    const toolbar = document.createElement('div');
    toolbar.className = 'code-toolbar';
    const label = document.createElement('span');
    label.className = 'code-language';
    label.textContent = language || 'text';
    const copy = document.createElement('button');
    copy.type = 'button';
    copy.className = 'code-copy';
    copy.textContent = 'Copy';
    copy.setAttribute('aria-label', 'Copy code');
    copy.addEventListener('click', async () => {
      try {
        await copyText(code.innerText);
        copy.textContent = 'Copied';
      } catch {
        copy.textContent = "Couldn't copy";
      }
      setTimeout(() => (copy.textContent = 'Copy'), 2000);
    });
    toolbar.append(label, copy);
    pre.prepend(toolbar);
  }
}

/** Copies `text` to the clipboard. localhost counts as a secure context, so the Clipboard API is available. */
export async function copyText(text) {
  await navigator.clipboard.writeText(text);
}

const pinnedThreshold = 80;

/**
 * Tracks whether the user keeps `element` scrolled to the bottom ("pinned"). It stays pinned unless
 * the user scrolls up more than 80 px, and tells .NET (`OnPinnedChanged`) whenever that changes.
 */
export function attachScroll(element, dotnet) {
  const state = { pinned: true };
  const onScroll = () => {
    const pinned = element.scrollHeight - element.scrollTop - element.clientHeight <= pinnedThreshold;
    if (pinned !== state.pinned) {
      state.pinned = pinned;
      dotnet.invokeMethodAsync('OnPinnedChanged', pinned);
    }
  };
  element.addEventListener('scroll', onScroll, { passive: true });
  element.localChatScroll = state;
  return {
    dispose: () => {
      element.removeEventListener('scroll', onScroll);
      delete element.localChatScroll;
    },
  };
}

/** Keeps a pinned `element` at the bottom as content grows. */
export function followIfPinned(element) {
  if (element?.localChatScroll?.pinned) {
    element.scrollTop = element.scrollHeight;
  }
}

/** Jumps to the bottom and pins `element` again. */
export function scrollToBottom(element) {
  if (!element) {
    return;
  }

  element.scrollTop = element.scrollHeight;
  if (element.localChatScroll) {
    element.localChatScroll.pinned = true;
  }
}

/**
 * Enter sends and Shift+Enter adds a newline. Blazor cannot cancel the default Enter behavior
 * conditionally, so this listener does it and calls .NET (`SendFromKeyboard`).
 */
export function attachComposer(textarea, dotnet) {
  const onKeyDown = event => {
    if (event.key === 'Enter' && !event.shiftKey && !event.isComposing) {
      event.preventDefault();
      dotnet.invokeMethodAsync('SendFromKeyboard');
    }
  };
  textarea.addEventListener('keydown', onKeyDown);
  return { dispose: () => textarea.removeEventListener('keydown', onKeyDown) };
}
